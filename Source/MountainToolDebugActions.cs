using LudeonTK;
using Verse;

namespace MountainTool
{
    public static class MountainToolDebugActions
    {
        [DebugAction("General", "Mountain Tool",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void MountainTool()
        {
            Dialog_MountainTool.Open();
        }
    }
}
