using MelonLoader;

[assembly: MelonInfo(typeof(KLINE_Standard.KLINEMod), "KLINE_Standard", "1.0.0", "Archie")]
[assembly: MelonGame("TVGS", "Schedule I")]
[assembly: HarmonyDontPatchAll]

namespace KLINE_Standard
{
  public class KLINEMod : MelonMod
  {
    public static readonly bool debugLog = false;

    public override void OnInitializeMelon()
    {
      try
      {
        HarmonyInstance.PatchAll();
        if (debugLog) MelonLogger.Msg("KLINE_Standard loaded!");
      }
      catch (Exception e)
      {
        MelonLogger.Error($"Failed to initialize KLINE_Standard: {e}");
      }
    }
  }
  public class KLINEUtilities
  {
    /// <summary>
    /// Converts a System.Collections.Generic.List<T> to an Il2CppSystem.Collections.Generic.List<T>.
    /// </summary>
    /// <typeparam name="T">The type of elements in the list, must inherit from Il2CppSystem.Object.</typeparam>
    /// <param name="systemList">The System list to convert.</param>
    /// <returns>An Il2CppSystem list containing the same elements, or an empty list if the input is null.</returns>
    public static Il2CppSystem.Collections.Generic.List<T> ConvertList<T>(List<T> systemList)
        where T : Il2CppSystem.Object
    {
      if (systemList == null)
        return new Il2CppSystem.Collections.Generic.List<T>();

      Il2CppSystem.Collections.Generic.List<T> il2cppList = new(systemList.Count);
      foreach (var item in systemList)
      {
        if (item != null)
          il2cppList.Add(item);
      }
      return il2cppList;
    }

    /// <summary>
    /// Converts an Il2CppSystem.Collections.Generic.List<T> to a System.Collections.Generic.List<T>.
    /// </summary>
    /// <typeparam name="T">The type of elements in the list, must inherit from Il2CppSystem.Object.</typeparam>
    /// <param name="il2cppList">The Il2CppSystem list to convert.</param>
    /// <returns>A System list containing the same elements, or an empty list if the input is null.</returns>
    public static List<T> ConvertList<T>(Il2CppSystem.Collections.Generic.List<T> il2cppList)
        where T : Il2CppSystem.Object
    {
      if (il2cppList == null)
        return [];

      List<T> systemList = new(il2cppList.Count);
      for (int i = 0; i < il2cppList.Count; i++)
      {
        var item = il2cppList[i];
        if (item != null)
          systemList.Add(item);
      }
      return systemList;
    }
  }
}