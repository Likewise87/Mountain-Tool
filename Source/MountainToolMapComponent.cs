using Verse;

namespace MountainTool
{
    /// <summary>Draws path previews in the map update loop where GenDraw is visible.</summary>
    public class MountainToolMapComponent : MapComponent
    {
        public MountainToolMapComponent(Map map) : base(map)
        {
        }

        public override void MapComponentUpdate()
        {
            if (Find.CurrentMap != map) return;
            MountainToolDraw.DrawWorldOverlays();
        }
    }
}
