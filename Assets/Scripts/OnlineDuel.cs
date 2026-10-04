using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

namespace Scripts
{
    public sealed class OnlineDuel : MonoBehaviour
    {
        private const string StateMessage = "cb.state.1", ChoiceMessage = "cb.choice.1", DirectionMessage = "cb.direction.1", ChargeMessage = "cb.charge.1";
        private const float PlanSeconds = 3f, SnapshotSeconds = .1f;
        private const int MaxPayload = 12000;
        [Serializable] internal struct FighterState : INetworkSerializeByMemcpy
        {
            public int move, serial, hp, stance, special, facing, force, queueCount;
            public float x, y, progress;
            public bool running;
        }
        [Serializable] internal sealed class State
        {
            public int sequence, window;
            public FighterState a, b;
            public int[] choices;
            public float remaining;
            public bool ended;
            public string result;
        }
        [Serializable] internal sealed class Choice { public int window, move, force; }
        private sealed class InputWindow
        {
            public int revision;
            public int sourceSerial;
            public Move sourceMove;
            public bool open;
            public double deadline;
            public int[] choices = Array.Empty<int>();
        }
        private ActorManager battle;
        private Actor[] actors;
        private Move[] moves;
        private readonly InputWindow[] windows = { new InputWindow(), new InputWindow() };
        private NetworkManager network;
        private UnityTransport transport;
        private OnlineDuelPlanner planner;
        private ISession session;
        private State latest;
        private string fingerprint, code = "", address = "127.0.0.1", status = "방을 만들거나 대전 찾기를 누르세요.";
        private bool busy, running, ended, registered, leaving, attempted, showLocal, cancelRequested;
        private ulong remote;
        private int sendSequence, receivedSequence, submittedWindow = -1;
        private double nextSnapshot, receivedAt, startedAt;
        private Vector2 scroll;
        private Vector2[] previousPositions = new Vector2[2];
        private bool autoTest;
        private double testStarted;
        private int acceptedInputs;
        private double endedAt;
        private long snapshotBytes;
        private int sentDirection = 99;
        private double nextDirectionSend;
        private int receivedDirectionMask;
        private int sentDirectionPackets, receivedDirectionPackets;
        private int lastDirectionPacketLength;

        private void Start()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var args = Environment.GetCommandLineArgs();
            autoTest = args.Contains("-duelAutoTest");
            testStarted = Time.realtimeSinceStartupAsDouble;
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-duelCode") code = args[i + 1];
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-duelRole") Connect(args[i + 1]);
#endif
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }
        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == "OnlineCombat" && FindFirstObjectByType<OnlineDuel>() == null)
                new GameObject("Online Duel").AddComponent<OnlineDuel>();
        }
        private void Awake()
        {
            battle = FindFirstObjectByType<ActorManager>();
            if (battle == null) { status = "전투 씬 설정이 없습니다."; enabled = false; return; }
            battle.autoSimulate = false;
            actors = new[] { battle.actorA, battle.actorB };
            var graph = actors[0].CombatMoveGraph;
            if (graph == null || actors[1].CombatMoveGraph != graph || string.IsNullOrEmpty(graph.networkContentHash))
            { status = "Tools > CrossBlade > Prepare Online Duel을 실행하세요."; return; }
            moves = graph.nodes.Where(n => n.move != null).Select(n => n.move).Distinct().ToArray();
            fingerprint = "CrossBlade-1:" + graph.networkContentHash;
            planner = ScriptableObject.CreateInstance<OnlineDuelPlanner>(); planner.duel = this;
            foreach (var actor in actors) actor.SetOnlinePlanner(planner);
            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None)) canvas.gameObject.SetActive(false);
            foreach (var click in FindObjectsByType<ActorForceClickTarget>(FindObjectsSortMode.None)) click.enabled = false;
            foreach (var hud in FindObjectsByType<CombatHudUI>(FindObjectsSortMode.None)) hud.enabled = false;
            var root = new GameObject("Online Network");
            transport = root.AddComponent<UnityTransport>();
            network = root.AddComponent<NetworkManager>();
            network.NetworkConfig = new NetworkConfig { NetworkTransport = transport, EnableSceneManagement = false,
                ConnectionApproval = true, PlayerPrefab = null, TickRate = 30 };
            network.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(fingerprint);
            network.ConnectionApprovalCallback = Approve;
            network.OnClientConnectedCallback += Connected;
            network.OnClientDisconnectCallback += Disconnected;
        }
        private void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            bool content = request.Payload != null && request.Payload.Length < 256 && Encoding.UTF8.GetString(request.Payload) == fingerprint;
            response.Approved = content && !ended && network.ConnectedClientsIds.Count < 2;
            response.CreatePlayerObject = false;
            response.Pending = false;
            response.Reason = content ? "방이 가득 찼거나 종료됐습니다." : "게임 데이터 버전이 다릅니다. 같은 빌드로 접속하세요.";
        }
        private void RegisterMessages()
        {
            if (registered) return;
            network.CustomMessagingManager.RegisterNamedMessageHandler(StateMessage, ReceiveState);
            network.CustomMessagingManager.RegisterNamedMessageHandler(ChoiceMessage, ReceiveChoice);
            network.CustomMessagingManager.RegisterNamedMessageHandler(DirectionMessage, ReceiveDirection);
            network.CustomMessagingManager.RegisterNamedMessageHandler(ChargeMessage, ReceiveCharge);
            registered = true;
        }
        private void Connected(ulong id)
        {
            if (cancelRequested || leaving) return;
            RegisterMessages();
            if (network.IsServer && id != NetworkManager.ServerClientId)
            {
                remote = id; running = true; status = "대전 중 · 당신은 왼쪽";
                startedAt = Time.realtimeSinceStartupAsDouble;
                Debug.Log("ONLINE_PEER_CONNECTED");
            }
            else if (!network.IsServer && id == network.LocalClientId)
            {
                status = "연결됨 · 당신은 오른쪽";
                foreach (var actor in actors)
                {
                    var body = actor.GetComponent<Rigidbody2D>();
                    if (body != null) body.simulated = false;
                }
            }
        }
        private void Disconnected(ulong id)
        {
            if (leaving || ended) return;
            running = false; ended = true;
            status = network.IsServer ? "상대 연결이 끊겨 경기가 중단됐습니다." :
                "연결 종료: " + (string.IsNullOrEmpty(network.DisconnectReason) ? "호스트에 연결할 수 없습니다." : network.DisconnectReason);
        }
        private async Task Authenticate()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                string profile = Application.isEditor ? "editor" : "player";
                var args = Environment.GetCommandLineArgs();
                for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-onlineProfile") profile = args[i + 1];
                await UnityServices.InitializeAsync(new InitializationOptions().SetProfile(profile));
            }
            if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
        private async void Connect(string mode)
        {
            if (busy || attempted || network == null) return;
            busy = true; attempted = true; status = "연결 준비 중…";
            try
            {
                if (mode.StartsWith("local"))
                {
                    transport.SetConnectionData(address, 7777, "0.0.0.0");
                    bool ok = mode == "local-host" ? network.StartHost() : network.StartClient();
                    if (!ok) throw new InvalidOperationException("로컬 연결을 시작하지 못했습니다.");
                }
                else
                {
                    await Authenticate();
                    if (cancelRequested) throw new OperationCanceledException();
                    var options = new SessionOptions { MaxPlayers = 2, Type = "CrossBladeDuel",
                        SessionProperties = new Dictionary<string, SessionProperty> {
                            { "build", new SessionProperty(fingerprint, VisibilityPropertyOptions.Public, PropertyIndex.String1) }
                        } }.WithRelayNetwork();
                    if (mode == "host") { options.IsPrivate = true; session = await MultiplayerService.Instance.CreateSessionAsync(options); }
                    else if (mode == "join") session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code.Trim().ToUpperInvariant());
                    else session = await MultiplayerService.Instance.MatchmakeSessionAsync(
                        new QuickJoinOptions { Timeout = TimeSpan.FromSeconds(5), CreateSession = true,
                            Filters = new List<FilterOption> {
                                new FilterOption(FilterField.StringIndex1, fingerprint, FilterOperation.Equal),
                                new FilterOption(FilterField.AvailableSlots, "1", FilterOperation.GreaterOrEqual)
                            } }, options);
                    code = session.Code;
                    Debug.Log("ONLINE_SESSION_READY: " + mode + " code=" + code);
                    if (cancelRequested) throw new OperationCanceledException();
                }
                RegisterMessages();
                if (!running && !ended) status = network.IsServer ? "상대 접속 대기 중" : "호스트 상태 수신 대기 중";
            }
            catch (Exception e)
            {
                status = cancelRequested ? "매칭을 취소했습니다." : "접속 실패: " + e.Message;
                ended = true; running = false;
                if (network != null) network.Shutdown();
                if (session != null) { try { await session.LeaveAsync(); } catch { } session = null; }
                Debug.LogWarning("ONLINE_CONNECT_FAILED: " + status);
            }
            finally { busy = false; }
        }
        internal PlanQueryState Plan(Actor actor)
        {
            int seat = actor == actors[0] ? 0 : 1;
            if (!actor.HasQueueSpace) { windows[seat].open = false; return PlanQueryState.Running; }
            if (actor.HasPlannedMove) { windows[seat].open = false; return PlanQueryState.Ready; }
            var window = windows[seat];
            if (window.open && (window.sourceSerial != actor.ActionController.NetworkActionSerial ||
                                window.sourceMove != actor.PlanningMove)) window.open = false;
            if (!window.open)
            {
                window.choices = actor.PlanningMove == null ? Array.Empty<int>() : actor.PlanningMove.After
                    .Where(m => m != null).Select(IndexOf).Where(i => i >= 0).Distinct().ToArray();
                if (window.choices.Length == 0)
                {
                    if (actor.QueueCount == 0 && actor.IdleMove != null)
                    { actor.SubmitPlannedMove(actor.IdleMove); return PlanQueryState.Ready; }
                    return PlanQueryState.Failed;
                }
                window.revision++; window.open = true; window.deadline = Time.realtimeSinceStartupAsDouble + PlanSeconds;
                window.sourceSerial = actor.ActionController.NetworkActionSerial;
                window.sourceMove = actor.PlanningMove;
            }
            if (Time.realtimeSinceStartupAsDouble >= window.deadline)
            {
                window.open = false;
                if (actor.QueueCount > 0) return PlanQueryState.Running;
                if (actor.IdleMove != null) actor.SubmitPlannedMove(actor.IdleMove);
                else actor.FailPlannedMove();
                return actor.HasPlannedMove ? PlanQueryState.Ready : PlanQueryState.Failed;
            }
            return PlanQueryState.Running;
        }
        private int IndexOf(Move move) => Array.IndexOf(moves, move);
        private bool Accept(int seat, Choice input)
        {
            var window = windows[seat];
            if (!running || ended || !window.open || input.window != window.revision || input.force != 1
                || window.sourceSerial != actors[seat].ActionController.NetworkActionSerial
                || window.sourceMove != actors[seat].PlanningMove || !actors[seat].HasQueueSpace
                || Time.realtimeSinceStartupAsDouble >= window.deadline || !window.choices.Contains(input.move)) return false;
            actors[seat].SubmitPlannedMove(moves[input.move]);
            window.open = false;
            acceptedInputs++;
            return true;
        }
        private void ReceiveChoice(ulong sender, FastBufferReader reader)
        {
            if (!network.IsServer || sender != remote || sender == NetworkManager.ServerClientId) return;
            try { var json = Read(reader); if (json.Length <= 256) Accept(1, JsonUtility.FromJson<Choice>(json)); }
            catch (Exception) { /* Invalid messages never affect combat. */ }
        }
        private void ReceiveDirection(ulong sender, FastBufferReader reader)
        {
            receivedDirectionPackets++;
            lastDirectionPacketLength = reader.Length;
            if (!network.IsServer || !running || sender != remote || sender == NetworkManager.ServerClientId) return;
            try
            {
                if (reader.Length < sizeof(int)) return;
                reader.ReadValueSafe(out int direction);
                if (direction >= -1 && direction <= 1)
                {
                    actors[1].SetDirectionalInput(actors[1].IsMoveRunning ? direction : 0);
                    if (direction < 0) receivedDirectionMask |= 1;
                    if (direction > 0) receivedDirectionMask |= 2;
                }
            }
            catch (Exception) { /* Invalid input never affects combat. */ }
        }
        private void ReceiveCharge(ulong sender, FastBufferReader reader)
        {
            if (!network.IsServer || !running || ended || sender != remote || sender == NetworkManager.ServerClientId) return;
            try
            {
                if (reader.Length < sizeof(int)) return;
                reader.ReadValueSafe(out int serial);
                if (serial == actors[1].ActionController.NetworkActionSerial)
                    actors[1].ActionController.TryChargeCurrent();
            }
            catch (Exception) { /* Invalid input never affects combat. */ }
        }
        private void SendDirection()
        {
            int direction = ActorManager.ReadArrowDirection();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (autoTest) direction = ((int)(Time.realtimeSinceStartupAsDouble - testStarted) % 2 == 0) ? -1 : 1;
#endif
            double now = Time.realtimeSinceStartupAsDouble;
            if (direction == sentDirection && now < nextDirectionSend) return;
            using var writer = new FastBufferWriter(sizeof(int), Allocator.Temp);
            writer.WriteValueSafe(direction);
            network.CustomMessagingManager.SendNamedMessage(DirectionMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
            sentDirectionPackets++;
            sentDirection = direction;
            nextDirectionSend = now + .2;
        }
        private void Choose(int move)
        {
            int revision = network.IsServer ? windows[0].revision : latest.window;
            var input = new Choice { move = move, force = 1, window = revision };
            if (network.IsServer) Accept(0, input);
            else Send(ChoiceMessage, NetworkManager.ServerClientId, JsonUtility.ToJson(input));
            submittedWindow = revision;
        }
        private static string Read(FastBufferReader reader)
        {
            if (reader.Length > MaxPayload) throw new InvalidOperationException();
            reader.ReadValueSafe(out string json); return json;
        }
        private void Send(string name, ulong target, string json)
        {
            using var writer = new FastBufferWriter(MaxPayload, Allocator.Temp);
            writer.WriteValueSafe(json);
            network.CustomMessagingManager.SendNamedMessage(name, target, writer, NetworkDelivery.ReliableSequenced);
        }
        private FighterState Capture(Actor actor) => new FighterState {
            move = IndexOf(actor.ActionController.NetworkSourceMove), serial = actor.ActionController.NetworkActionSerial,
            hp = actor.Hp, stance = actor.Stance, special = actor.SpecialForce, facing = actor.FacingSign,
            force = actor.ActionController.CurrentForce, queueCount = actor.QueueCount,
            x = actor.Position.x, y = actor.Position.y, progress = actor.MoveProgress, running = actor.IsMoveRunning };
        private void FixedUpdate()
        {
            if (network == null || !network.IsServer || !running || ended) return;
            actors[0].SetDirectionalInput(actors[0].IsMoveRunning ? ActorManager.ReadArrowDirection() : 0);
            for (int i = 0; i < actors.Length; i++)
                if (windows[i].open && (windows[i].sourceSerial != actors[i].ActionController.NetworkActionSerial ||
                                        windows[i].sourceMove != actors[i].PlanningMove))
                    windows[i].open = false;
            battle.Simulate(Time.fixedDeltaTime);
            if (actors.Any(a => a.Hp <= 0))
            {
                ended = true;
                status = actors.All(a => a.Hp <= 0) ? "무승부" : actors[0].Hp > 0 ? "왼쪽 승리" : "오른쪽 승리";
            }
            else if (Time.realtimeSinceStartupAsDouble - startedAt > 300)
            { ended = true; status = "5분 제한 종료 · 무승부"; }
        }
        private void Update()
        {
            TickAutoTest();
            if (network == null || !registered) return;
            if (running && !ended && Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                if (network.IsServer) actors[0].ActionController.TryChargeCurrent();
                else if (latest != null && latest.b.running)
                {
                    using var writer = new FastBufferWriter(sizeof(int), Allocator.Temp);
                    writer.WriteValueSafe(latest.b.serial);
                    network.CustomMessagingManager.SendNamedMessage(ChargeMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
                }
            }
            if (!network.IsServer && running && !ended) SendDirection();
            if (ended)
            {
                if (endedAt == 0) endedAt = Time.realtimeSinceStartupAsDouble;
                if (!busy && !leaving && Time.realtimeSinceStartupAsDouble - endedAt > 10)
                { ReleaseConnection(); return; }
            }
            if (network.IsServer && running && network.ConnectedClientsIds.Contains(remote) && Time.realtimeSinceStartupAsDouble >= nextSnapshot)
            {
                var w = windows[1];
                var state = new State { sequence = ++sendSequence, a = Capture(actors[0]), b = Capture(actors[1]),
                    window = w.revision, choices = w.open ? w.choices : Array.Empty<int>(), remaining = (float)Math.Max(0, w.deadline - Time.realtimeSinceStartupAsDouble), ended = ended, result = ended ? status : "" };
                SendState(state);
                nextSnapshot = Time.realtimeSinceStartupAsDouble + (ended ? 1f : SnapshotSeconds);
            }
            else if (!network.IsServer && latest != null && !ended)
            {
                Present(actors[0], latest.a, 0); Present(actors[1], latest.b, 1);
            }
        }
        private void ReceiveState(ulong sender, FastBufferReader reader)
        {
            if (network.IsServer || sender != NetworkManager.ServerClientId || ended) return;
            try
            {
                var state = ReadState(reader);
                if (state.sequence <= receivedSequence) return;
                previousPositions[0] = actors[0].Position; previousPositions[1] = actors[1].Position;
                latest = state; receivedAt = Time.realtimeSinceStartupAsDouble; receivedSequence = state.sequence;
                if (receivedSequence == 1) Debug.Log("ONLINE_FIRST_STATE_RECEIVED");
                running = true; status = state.ended ? state.result : "대전 중 · 당신은 오른쪽";
                Present(actors[0], latest.a, 0); Present(actors[1], latest.b, 1);
                ended = state.ended;
            }
            catch (Exception e) { Debug.LogWarning("온라인 상태를 읽지 못했습니다: " + e.GetType().Name); }
        }
        private void Present(Actor actor, FighterState state, int seat)
        {
            float age = (float)(Time.realtimeSinceStartupAsDouble - receivedAt);
            var position = Vector2.Lerp(previousPositions[seat], new Vector2(state.x, state.y), Mathf.Clamp01(age / SnapshotSeconds));
            if (latest.ended) position = new Vector2(state.x, state.y);
            actor.ApplyOnlineState(state.hp, state.stance, state.special, position, state.facing);
            var move = state.move >= 0 && state.move < moves.Length ? moves[state.move] : null;
            float progress = state.progress;
            if (state.running && move != null) progress += Mathf.Min(age, .15f) / Mathf.Max(.01f, move.Duration + actor.MoveStartDelay);
            actor.ActionController.ApplyReplica(move, state.serial, state.running, Mathf.Clamp01(progress));
        }
        private void SendState(State state)
        {
            using var writer = new FastBufferWriter(MaxPayload, Allocator.Temp);
            writer.WriteValueSafe(state.sequence); writer.WriteValueSafe(state.a); writer.WriteValueSafe(state.b);
            writer.WriteValueSafe(state.window); writer.WriteValueSafe(state.remaining); writer.WriteValueSafe(state.ended);
            writer.WriteValueSafe(state.choices.Length);
            foreach (var choice in state.choices) writer.WriteValueSafe(choice);
            writer.WriteValueSafe(state.result ?? "");
            snapshotBytes += writer.Length;
            network.CustomMessagingManager.SendNamedMessage(StateMessage, remote, writer, NetworkDelivery.ReliableSequenced);
        }
        private static State ReadState(FastBufferReader reader)
        {
            if (reader.Length > MaxPayload) throw new InvalidOperationException();
            var state = new State();
            reader.ReadValueSafe(out state.sequence); reader.ReadValueSafe(out state.a); reader.ReadValueSafe(out state.b);
            reader.ReadValueSafe(out state.window); reader.ReadValueSafe(out state.remaining); reader.ReadValueSafe(out state.ended);
            reader.ReadValueSafe(out int count);
            if (count < 0 || count > 256) throw new InvalidOperationException();
            state.choices = new int[count];
            for (int i = 0; i < count; i++) reader.ReadValueSafe(out state.choices[i]);
            reader.ReadValueSafe(out state.result);
            return state;
        }
        private void TickAutoTest()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!autoTest || network == null) return;
            if (running && !ended)
            {
                var choices = network.IsServer ? windows[0].open ? windows[0].choices : Array.Empty<int>() : latest?.choices ?? Array.Empty<int>();
                int revision = network.IsServer ? windows[0].revision : latest?.window ?? -1;
                if (choices.Length > 0 && submittedWindow != revision) Choose(choices[revision % choices.Length]);
            }
            if (Time.realtimeSinceStartupAsDouble - testStarted > 20)
            {
                bool ok = network.IsServer ? acceptedInputs >= 2 && sendSequence >= 10 && receivedDirectionMask == 3 : receivedSequence >= 10;
                Debug.Log($"ONLINE_PROCESS_TEST {(ok ? "PASS" : "FAIL")} host={network.IsServer} inputs={acceptedInputs} sent={sendSequence} received={receivedSequence} directions={receivedDirectionMask} directionPackets={sentDirectionPackets}/{receivedDirectionPackets} lastDirectionLength={lastDirectionPacketLength} payloadBytes={snapshotBytes}");
                autoTest = false; FinishTest(ok);
            }
#endif
        }
        private async void FinishTest(bool ok)
        {
            leaving = true; running = false;
            try { if (session != null) await session.LeaveAsync(); } catch (Exception) { }
            if (network != null) network.Shutdown();
            Application.Quit(ok ? 0 : 1);
        }
        private async void Leave()
        {
            if (busy || leaving) return;
            leaving = true; running = false;
            try { if (session != null) await session.LeaveAsync(); }
            catch (Exception) { }
            if (network != null) { network.Shutdown(); Destroy(network.gameObject); }
            SceneManager.LoadScene("OnlineCombat");
        }
        private async void ReleaseConnection()
        {
            leaving = true; running = false;
            try { if (session != null) await session.LeaveAsync(); }
            catch (Exception) { }
            session = null;
            if (network != null) { network.Shutdown(); Destroy(network.gameObject); network = null; }
            leaving = false;
        }
        private void OnDestroy()
        {
            if (network != null) { network.Shutdown(); Destroy(network.gameObject); }
            if (planner != null) Destroy(planner);
        }
        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(12, 12, Mathf.Min(460, Screen.width - 24), Screen.height - 24), GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("CrossBlade · 온라인 1대1 테스트");
            GUILayout.Label(status);
            if (!attempted)
            {
                GUI.enabled = !busy && network != null;
                if (GUILayout.Button("대전 찾기 (Quick Join)")) Connect("quick");
                if (GUILayout.Button("비공개 방 만들기")) Connect("host");
                code = GUILayout.TextField(code, 16);
                if (GUILayout.Button("방 코드로 참가")) Connect("join");
                showLocal = GUILayout.Toggle(showLocal, "개발용 로컬 접속");
                if (showLocal)
                {
                    address = GUILayout.TextField(address, 64);
                    if (GUILayout.Button("로컬 호스트 :7777")) Connect("local-host");
                    if (GUILayout.Button("로컬 참가")) Connect("local-join");
                }
                GUI.enabled = true;
            }
            else
            {
                if (!string.IsNullOrEmpty(code)) { GUILayout.Label("방 코드: " + code); if (GUILayout.Button("코드 복사")) GUIUtility.systemCopyBuffer = code; }
                if (actors != null)
                    GUILayout.Label($"왼쪽 HP {actors[0].Hp} / 자세 {actors[0].Stance}     오른쪽 HP {actors[1].Hp} / 자세 {actors[1].Stance}");
                if (running && !ended)
                {
                    bool host = network.IsServer;
                    var choices = host ? windows[0].open ? windows[0].choices : Array.Empty<int>() : latest?.choices ?? Array.Empty<int>();
                    int revision = host ? windows[0].revision : latest?.window ?? -1;
                    float remaining = host ? (float)(windows[0].deadline - Time.realtimeSinceStartupAsDouble) : latest == null ? 0 : latest.remaining - (float)(Time.realtimeSinceStartupAsDouble - receivedAt);
                    int force = host ? actors[0].ActionController.CurrentForce : latest?.b.force ?? 0;
                    int queueCount = host ? actors[0].QueueCount : latest?.b.queueCount ?? 0;
                    GUILayout.Label($"현재 힘 {force} · 다음 행동 선택 전 스페이스: 힘 +1 (공격 사용 시 자세 5)");
                    GUILayout.Label($"대기 큐 {queueCount}/{actors[0].MaxQueuedMoves} · 지금 추가할 행동의 종료 시 자세 회복 +{queueCount * 5}");
                    if (choices.Length > 0) GUILayout.Label($"다음 행동 선택 · {Mathf.Max(0, remaining):0.0}초 (시간 초과: Idle 복귀)");
                    GUI.enabled = submittedWindow != revision && remaining > 0 && queueCount < actors[0].MaxQueuedMoves;
                    foreach (int index in choices)
                        if (index >= 0 && index < moves.Length && GUILayout.Button(moves[index].name)) Choose(index);
                    GUI.enabled = true;
                }
                if (busy && GUILayout.Button(cancelRequested ? "취소 요청 중…" : "매칭 / 접속 취소"))
                { cancelRequested = true; running = false; }
                GUI.enabled = !busy && !leaving;
                if (GUILayout.Button("나가기 / 다시 시작")) Leave();
                GUI.enabled = true;
            }
            GUILayout.EndScrollView(); GUILayout.EndArea();
        }
    }
}
