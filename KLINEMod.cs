using MelonLoader;

[assembly: MelonInfo(typeof(KLINE.KLINEMod), "KLINE", "1.0.0", "Archie")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace KLINE
{
  public class KLINEMod : MelonMod
  {
    public static readonly bool debugLog = false;

    public override void OnInitializeMelon()
    {
      try
      {
        HarmonyInstance.PatchAll();
        if (debugLog) MelonLogger.Msg("KLINE loaded!");
      }
      catch (Exception e)
      {
        MelonLogger.Error($"Failed to initialize KLINE: {e}");
      }
    }
  }
}