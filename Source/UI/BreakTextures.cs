using UnityEngine;
using Verse;

namespace BreakTimer
{
    // Loaded once at startup so we never look them up per frame.
    [StaticConstructorOnStartup]
    public static class BreakTextures
    {
        public static readonly Texture2D LightningBolt =
            ContentFinder<Texture2D>.Get("UI/BreakTimer/LightningBolt", reportFailure: true);
    }
}
