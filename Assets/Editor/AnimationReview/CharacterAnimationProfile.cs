using System;
using UnityEditor;
using UnityEngine;

namespace CrossBlade.EditorTools
{
    [CreateAssetMenu(menuName = "CrossBlade/Animation Review Profile")]
    public sealed class CharacterAnimationProfile : ScriptableObject
    {
        public GameObject previewPrefab;
        [Tooltip("Validated Generic FBX whose import settings are used for new animations.")]
        public GameObject importBaseline;
        public AnimationClip referenceClip;
        public DefaultAsset incomingFolder;
        public DefaultAsset approvedFolder;
        [Tooltip("Paths relative to CharacterAnimationPlayer.animationRoot. Object curves at/below these paths are excluded; bone animation remains.")]
        public string[] fixedAttachmentPaths = Array.Empty<string>();
        public string IncomingPath => AssetDatabase.GetAssetPath(incomingFolder);
        public string ApprovedPath => AssetDatabase.GetAssetPath(approvedFolder);
        public static bool IsInside(string path, string folder) => !string.IsNullOrEmpty(folder)
            && path.StartsWith(folder.TrimEnd('/') + "/", StringComparison.Ordinal);
        public bool IsFixed(string path) => Array.Exists(fixedAttachmentPaths ?? Array.Empty<string>(),
            p => !string.IsNullOrEmpty(p) && (path == p || IsInside(path, p)));
    }
}
