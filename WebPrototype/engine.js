/* Crossblade Duel Lab: deterministic combat engine, independent of rendering. */
(function(root,factory){const api=factory();if(typeof module==='object'&&module.exports)module.exports=api;else root.Crossblade=api;})(typeof globalThis!=='undefined'?globalThis:this,function(){
'use strict';
const DEFAULTS=Object.freeze({maxHP:100,maxPosture:100,maxCharge:5,energyPerPosture:1,hasteCost:25,strikeCost:25,recovery:5,waitBonus:10,guardBonus:8,guardDamageStep:20,baseGuardTurns:1,jabRecovery:1,heavyRecovery:1,stunTurns:1,clashRate:.25});
const ACTIONS=Object.freeze(['wait','charge','jab','heavy','guard']),SKILLS=Object.freeze(['none','haste','strike']);
const clone=o=>JSON.parse(JSON.stringify(o));
const integer=(n,d,lo,hi)=>Number.isFinite(+n)?Math.max(lo,Math.min(hi,Math.floor(+n))):d;
function rules(o={}){const r={...DEFAULTS};for(const k of ['hasteCost','strikeCost'])r[k]=integer(o[k],r[k],1,200);r.recovery=integer(o.recovery,r.recovery,0,30);return r;}
function createGame(o={}){const r=rules(o.rules||o),energy=integer(o.startEnergy,0,0,1000);return {version:'0.3.1',turn:1,players:[0,1].map(id=>({id,hp:r.maxHP,posture:r.maxPosture,energy,charge:0,forced:null,pending:null})),winner:null,rules:r};}
function actionCost(a,f){return ({wait:0,charge:3,jab:2,heavy:3,guard:1}[a]||0)*integer(f,1,1,5);}
function skillCost(s,r=DEFAULTS){return s==='haste'?r.hasteCost:s==='strike'?r.strikeCost:0;}
function guardTurns(damage,base=1,step=20){return Math.max(0,base)+Math.floor(Math.max(0,damage)/step);}
function attackStats(a,f,c=0){return a==='jab'?{posture:12+4*f,hp:6+2*f}:a==='heavy'?{posture:20+4*c+4*f,hp:12+4*c+2*f}:{posture:0,hp:0};}
function statusPlan(p){if(p.forced&&p.forced.turns>0)return {action:p.forced.kind,force:3,forced:true,fromQueue:false};if(p.pending)return {...p.pending,forced:false,fromQueue:true};return null;}
function preview(p,c={},r=DEFAULTS){const fixed=statusPlan(p),action=fixed?fixed.action:(ACTIONS.includes(c.action)?c.action:'wait'),force=fixed?fixed.force:integer(c.force,3,1,5),cost=fixed?.forced?0:actionCost(action,force);return {action,force,forced:!!fixed?.forced,fromQueue:!!fixed?.fromQueue,cost,gain:cost*r.energyPerPosture,affordable:cost<=p.posture,stats:attackStats(action,force,p.charge)};}
function mergeForced(a,b){if(!a||a.turns<=0)return b?.turns>0?{...b}:null;if(!b||b.turns<=0)return {...a};if(a.kind===b.kind)return {...b,turns:Math.max(a.turns,b.turns)};const rank={recovery:1,guard:2,stun:3};return rank[a.kind]>rank[b.kind]?{...a}:{...b};}
function resolveTurn(input,choices){
 if(!input||!Array.isArray(input.players)||input.players.length!==2)throw new TypeError('A two-player state is required.');
 if(!Array.isArray(choices)||choices.length!==2)throw new TypeError('Two committed choices are required.');
 if(input.winner!==null)return {state:clone(input),events:[],performed:[]};
 const state=clone(input),r=state.rules||rules(),p=state.players,events=[];
 const emit=(type,player,extra={})=>events.push({type,player,...extra});
 const plans=p.map((x,i)=>{const fixed=statusPlan(x),c=choices[i]||{};return {action:fixed?fixed.action:(ACTIONS.includes(c.action)?c.action:'wait'),force:fixed?fixed.force:integer(c.force,3,1,5),forced:!!fixed?.forced,fromQueue:!!fixed?.fromQueue,skill:SKILLS.includes(c.skill)?c.skill:'none',chargeUsed:x.charge};});
 const frozen=[false,false],hitInterrupt=[false,false],guardDamage=[0,0],guardBase=[null,null],newStatus=[null,null],activeStun=p.map(x=>x.forced?.kind==='stun'),oldForced=p.map(x=>x.forced?{...x.forced}:null);
 const performed=plans.map(x=>({...x,energySpent:0,energyGained:0,postureSpent:0,deferred:false}));
 function impose(i,kind,turns,reason){newStatus[i]=mergeForced(newStatus[i],{kind,turns,reason});}
 function interrupt(i,reason){hitInterrupt[i]=true;p[i].charge=0;if(p[i].pending||frozen[i])emit('queueInterrupted',i,{reason});p[i].pending=null;}
 function block(i,amount,base,source){guardDamage[i]+=amount;guardBase[i]=Math.max(guardBase[i]??0,base);emit('postureHit',i,{amount,source});}
 // Pay specials from energy held before the ordinary motion. Both committed
 // specials execute; stopping an ordinary motion does not stop its special.
 for(let i=0;i<2;i++){const cost=skillCost(plans[i].skill,r);if(p[i].energy<cost){emit('skillUnavailable',i,{skill:plans[i].skill,required:cost});plans[i].skill='none';performed[i].skill='none';}else if(cost>0){p[i].energy-=cost;performed[i].energySpent=cost;emit('skillUsed',i,{skill:plans[i].skill,cost});}}
 for(let i=0;i<2;i++)if(plans[i].skill==='haste'){frozen[1-i]=true;emit('haste',i,{target:1-i});}
 for(let i=0;i<2;i++)if(plans[i].skill==='strike'){
  const j=1-i;
  if(p[j].posture<=0||activeStun[j]){p[j].hp=Math.max(0,p[j].hp-1);emit('strikeHP',j,{amount:1,source:i});}
  else{const loss=Math.min(1,p[j].posture);p[j].posture-=loss;block(j,loss,r.baseGuardTurns,'strike');interrupt(j,'strike');
   if(p[j].posture===0){plans[j].action='stun';plans[j].forced=true;activeStun[j]=true;impose(j,'stun',r.stunTurns,'break');emit('break',j,{source:i});}
   else{plans[j].action='guard';plans[j].force=3;plans[j].forced=true;emit('strikeGuard',j,{source:i});}
  }
 }
 // Spend and gain only for motions that actually execute. Deferred commands
 // retain their original action and force; interrupted commands do not.
 for(let i=0;i<2;i++){
  const plan=plans[i];performed[i].action=plan.action;performed[i].force=plan.force;performed[i].forced=plan.forced;
  if(frozen[i]){performed[i].deferred=true;emit('deferred',i,{action:plan.action,force:plan.force});continue;}
  if(p[i].hp<=0)continue;
  if(plan.action==='heavy'&&p[i].charge<=0){plan.action='wait';plan.force=1;plan.forced=false;emit('noCharge',i);}
  if(!plan.forced){const perForce=actionCost(plan.action,1);if(perForce>0&&actionCost(plan.action,plan.force)>p[i].posture){const affordable=Math.floor(p[i].posture/perForce);if(affordable<1){plan.action='wait';plan.force=1;emit('noPosture',i);}else{emit('forceReduced',i,{from:plan.force,to:affordable});plan.force=affordable;}}
   const cost=actionCost(plan.action,plan.force);if(cost>0){p[i].posture-=cost;const gain=cost*r.energyPerPosture;p[i].energy+=gain;performed[i].postureSpent=cost;performed[i].energyGained=gain;emit('energy',i,{amount:gain,cost});}
  }
  performed[i].action=plan.action;performed[i].force=plan.force;
  if(plan.action==='charge'){const before=p[i].charge;p[i].charge=Math.min(r.maxCharge,p[i].charge+plan.force);emit('charge',i,{amount:p[i].charge-before,total:p[i].charge});}
 }
 const attacking=plans.map((x,i)=>!frozen[i]&&p[i].hp>0&&(x.action==='jab'||x.action==='heavy'));
 const stats=plans.map(x=>attackStats(x.action,x.force,x.chargeUsed)),isClash=attacking[0]&&attacking[1];
 if(isClash)emit('clash',-1,{rate:r.clashRate});
 // Capture outgoing attacks before damage, so mutual hits and double KO work.
 for(let i=0;i<2;i++)if(attacking[i]){
  const j=1-i,target=plans[j],raw=stats[i];
  if(p[j].posture<=0||activeStun[j]){const damage=isClash?Math.max(1,Math.ceil(raw.hp*r.clashRate)):raw.hp;p[j].hp=Math.max(0,p[j].hp-damage);interrupt(j,'hp');impose(j,'stun',r.stunTurns,'hit');emit('hpHit',j,{amount:damage,source:i,clash:isClash});}
  else{
   let factor,base=0,mode;
   if(isClash){factor=r.clashRate;mode='clash';}
   else if(!frozen[j]&&target.action==='guard'&&!target.forced){factor=plans[i].action==='jab'?(.18-.02*target.force):(.85-.04*target.force);mode='manual';}
   else if(!frozen[j]&&target.action==='guard'&&target.forced){factor=plans[i].action==='jab'?.45:.65;base=r.baseGuardTurns;mode='forced';}
   else{factor=plans[i].action==='jab'?.70:.80;base=r.baseGuardTurns;mode='auto';}
   const damage=Math.max(1,Math.ceil(raw.posture*factor-1e-9)),loss=Math.min(damage,p[j].posture);p[j].posture-=loss;
   if(!isClash){block(j,loss,base,mode);interrupt(j,'guard');}else emit('postureHit',j,{amount:loss,source:'clash'});
   if(p[j].posture===0){interrupt(j,'break');impose(j,'stun',r.stunTurns,'break');emit('break',j,{source:i});}
   emit('blockDetail',j,{raw:raw.posture,damage:loss,factor,mode});
  }
 }
 for(let i=0;i<2;i++){
  if(!frozen[i]&&attacking[i]){if(plans[i].action==='heavy')p[i].charge=0;impose(i,'recovery',plans[i].action==='jab'?r.jabRecovery:r.heavyRecovery,'attack');}
  if(guardBase[i]!==null&&p[i].posture>0){const base=guardBase[i],turns=guardTurns(guardDamage[i],base,r.guardDamageStep);if(turns>0){impose(i,'guard',turns,'guard');emit('guardLock',i,{damage:guardDamage[i],base,bonus:Math.floor(guardDamage[i]/r.guardDamageStep),turns});}else emit('guardFree',i,{damage:guardDamage[i]});}
  // Tick OLD forced time only, never the newly imposed next-turn duration.
  const carried=oldForced[i]?{...oldForced[i],turns:oldForced[i].turns-(frozen[i]?0:1)}:null;
  p[i].forced=mergeForced(carried,newStatus[i]);
  if(frozen[i]&&!hitInterrupt[i]&&!p[i].forced){p[i].pending={action:plans[i].action,force:plans[i].force};emit('queue',i,{...p[i].pending});}
  else if(!frozen[i]||hitInterrupt[i]||p[i].forced)p[i].pending=null;
  if(!frozen[i]&&p[i].hp>0){let amount=r.recovery;if(!hitInterrupt[i]&&!plans[i].forced){if(plans[i].action==='wait')amount+=r.waitBonus;if(plans[i].action==='guard')amount+=r.guardBonus;}const before=p[i].posture;p[i].posture=Math.min(r.maxPosture,p[i].posture+amount);if(p[i].posture>before)emit('recover',i,{amount:p[i].posture-before});}
  if(!frozen[i]&&p[i].hp>0&&hitInterrupt[i]&&plans[i].action==='guard'&&!plans[i].forced&&newStatus[i]?.kind!=='stun'){const before=p[i].posture;p[i].posture=Math.min(r.maxPosture,p[i].posture+r.guardBonus);if(p[i].posture>before)emit('recover',i,{amount:p[i].posture-before,bonus:true});}
 }
 if(p[0].hp<=0&&p[1].hp<=0)state.winner='draw';else if(p[0].hp<=0)state.winner=1;else if(p[1].hp<=0)state.winner=0;
 if(state.winner!==null)emit('victory',-1,{winner:state.winner});
 state.turn++;return {state,events,performed};
}
return Object.freeze({DEFAULTS,ACTIONS,SKILLS,rules,createGame,actionCost,skillCost,guardTurns,attackStats,statusPlan,preview,resolveTurn});
});
