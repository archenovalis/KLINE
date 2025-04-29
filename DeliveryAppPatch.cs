using HarmonyLib;
using ScheduleOne.Delivery;
using ScheduleOne.UI.Phone.Delivery;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using MelonLoader;
using UnityEngine.EventSystems;

namespace KLINE
{
  [HarmonyPatch(typeof(DeliveryApp))]
  public class DeliveryAppPatch
  { }
}