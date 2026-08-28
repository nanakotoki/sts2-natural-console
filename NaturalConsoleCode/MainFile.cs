using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace NaturalConsole.NaturalConsoleCode;

//You're recommended but not required to keep all your code in this package and all your assets in the NaturalConsole folder.
[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "NaturalConsole"; //At the moment, this is used only for the Logger and harmony names.

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } = new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);

    public static void Initialize()
    {
        var assembly = Assembly.GetExecutingAssembly();

        //If you want to use scripts defined in your mod for Godot scenes, uncomment the following line.
        //Godot.Bridge.ScriptManagerBridge.LookupScriptsInAssembly(assembly);

        Harmony harmony = new(ModId);

        harmony.PatchAll(assembly);

        try
        {
            ConsoleExecutor.Init();
        }
        catch (System.Exception e)
        {
            Logger.Error("ConsoleExecutor.Init failed: " + e);
        }

        try
        {
            UiController.TryCreate();
        }
        catch (System.Exception e)
        {
            Logger.Error("UiController.TryCreate failed: " + e);
        }
    }
}
