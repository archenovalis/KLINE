using MelonLoader;
using HarmonyLib;
using System;
using System.Collections.Generic;
using ScheduleOne.Delivery;
using ScheduleOne.UI.Phone.Delivery;

[assembly: MelonInfo(typeof(KLINE.KLINEMod), "KLINE", "1.0.0", "Archie")]
[assembly: MelonGame("TVGS", "Schedule I")]
[assembly: HarmonyDontPatchAll]
namespace KLINE
{
  public static class BuildInfo
  {
    public const string Name = "KLINE";
    public const string Description = "KLINE, a family-owned business, is the leading distributor of shipping, industrial, materials, and ingredients to businesses throughout the universe.";
    public const string Author = "Archie";
    public const string Version = "1.0.0";
  }
  public class KLINEMod : MelonMod
  {
    public override void OnInitializeMelon()
    {
      try
      {
        HarmonyInstance.PatchAll();
        MelonLogger.Msg("KLINE_Alternative loaded!");
      }
      catch (Exception e)
      {
        MelonLogger.Error($"Failed to initialize KLINE_Alternative: {e}");
      }
    }
  }
}