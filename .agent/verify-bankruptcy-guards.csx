using System;
using System.Reflection;
using SignatureFix;
using Unity.Entities;

var type = typeof(SignatureFix.SignatureFixSystem);
var flags = BindingFlags.Static | BindingFlags.NonPublic;
var purchaseMethod = type.GetMethod("IsPriorityPurchaseSafe", flags);
var bankruptcyMethod = type.GetMethod(
    "IsMatureBankruptcy",
    flags,
    null,
    new[] { typeof(int), typeof(int), typeof(uint), typeof(uint) },
    null);
var observeMethod = type.GetMethod(
    "ObserveCompany",
    flags,
    null,
    new[] { typeof(SignatureCompanyHistory), typeof(Entity), typeof(CompanyDepartureReason) },
    null);
var reasonMethod = typeof(VehicleDetailsUISystem).GetMethod("GetDepartureReason", flags);
var startingResourceMethod = type.GetMethod("GetStartingResourceBonus", flags);
var minimumTruckLoadMethod = type.GetMethod("GetMinimumTruckLoad", flags);
var fullLoadPurchaseMethod = type.GetMethod("GetFullLoadPurchaseAmount", flags);
var inputTargetMethod = type.GetMethod("GetInputTargetAmount", flags);
var selectInputMethod = type.GetMethod("ShouldSelectInput", flags);
if (purchaseMethod == null || bankruptcyMethod == null || observeMethod == null || reasonMethod == null || startingResourceMethod == null ||
    minimumTruckLoadMethod == null || fullLoadPurchaseMethod == null || inputTargetMethod == null || selectInputMethod == null)
    throw new Exception("Compiled guard helper missing.");

bool Safe(int worth, int limit, float price, int amount) =>
    (bool)purchaseMethod.Invoke(null, new object[] { worth, limit, price, amount });

bool Bankrupt(int worth, int limit, uint since, uint frame) =>
    (bool)bankruptcyMethod.Invoke(null, new object[] { worth, limit, since, frame });

int StartingResourceBonus(int amount) =>
    (int)startingResourceMethod.Invoke(null, new object[] { amount });

int MinimumTruckLoad(int capacity) =>
    (int)minimumTruckLoadMethod.Invoke(null, new object[] { capacity });

int FullLoadPurchase(int capacity, int headroom) =>
    (int)fullLoadPurchaseMethod.Invoke(null, new object[] { capacity, headroom });

int InputTarget(int totalTarget, int weight, int totalWeight) =>
    (int)inputTargetMethod.Invoke(null, new object[] { totalTarget, weight, totalWeight });

bool SelectInput(int candidateAmount, int candidateTarget, int selectedAmount, int selectedTarget) =>
    (bool)selectInputMethod.Invoke(null, new object[] { candidateAmount, candidateTarget, selectedAmount, selectedTarget });

if (!Safe(1000, 500, 2f, 250)) throw new Exception("Purchase at the threshold should be allowed.");
if (Safe(999, 500, 2f, 250)) throw new Exception("Purchase below the threshold should be blocked.");
if (Safe(1000, 500, 2f, 0)) throw new Exception("Empty purchases should be blocked.");
if (!Safe(-100, -500, -5f, 1)) throw new Exception("Negative prices must not create a reserve.");

if (Bankrupt(500, 500, 1, 65538)) throw new Exception("A solvent company must not be bankrupt.");
if (Bankrupt(499, 500, 0, 65537)) throw new Exception("A company without a low-income timer must not be bankrupt.");
if (Bankrupt(499, 500, 1, 65537)) throw new Exception("The complete grace period must be preserved.");
if (!Bankrupt(499, 500, 1, 65538)) throw new Exception("A mature genuine bankruptcy must be allowed.");

if (StartingResourceBonus(15000) != 15000) throw new Exception("A normal starting resource stack must be doubled.");
if (StartingResourceBonus(0) != 0) throw new Exception("An empty starting resource stack must remain empty.");
if (StartingResourceBonus(-1) != 0) throw new Exception("A negative resource amount must not receive a bonus.");
if (StartingResourceBonus(int.MaxValue) != 0) throw new Exception("A full resource stack must not overflow.");
if (StartingResourceBonus(int.MaxValue - 10) != 10) throw new Exception("A large resource stack must clamp at the integer limit.");

if (MinimumTruckLoad(20000) != 15000) throw new Exception("The minimum load must be 75% of truck capacity.");
if (MinimumTruckLoad(1) != 1) throw new Exception("Small capacities must round the minimum load upward.");
if (MinimumTruckLoad(0) != 0) throw new Exception("A missing truck capacity must not create a purchase.");
if (FullLoadPurchase(20000, 50000) != 20000) throw new Exception("Available headroom must request a full truck.");
if (FullLoadPurchase(20000, 16000) != 16000) throw new Exception("A safe partial request above 75% must use the available headroom.");
if (FullLoadPurchase(20000, 14999) != 0) throw new Exception("A request below 75% must be skipped.");
if (FullLoadPurchase(0, 50000) != 0) throw new Exception("Zero-capacity vehicles must not create a purchase.");

if (InputTarget(100000, 1, 4) != 25000) throw new Exception("Input targets must follow the recipe ratio.");
if (InputTarget(100000, 3, 4) != 75000) throw new Exception("Input targets must preserve the total target allocation.");
if (InputTarget(100000, 0, 4) != 0) throw new Exception("Invalid input weights must not receive a target.");
if (!SelectInput(20000, 80000, 10000, 20000)) throw new Exception("Lower recipe coverage must win even with more raw stock.");
if (SelectInput(10000, 20000, 20000, 80000)) throw new Exception("Higher recipe coverage must not replace the lower input.");
if (SelectInput(20000, 20000, 0, 0)) throw new Exception("An input already at target must not be selected.");
if (!SelectInput(10000, 20000, 0, 0)) throw new Exception("The first under-target input must be selected.");

var first = new Entity { Index = 10, Version = 1 };
var second = new Entity { Index = 11, Version = 1 };
var third = new Entity { Index = 12, Version = 1 };
var history = (SignatureCompanyHistory)observeMethod.Invoke(null, new object[] { new SignatureCompanyHistory(Entity.Null), first, CompanyDepartureReason.ExternalOrLoadReplacement });
if (history.m_CurrentCompany != first || history.m_LastReason != CompanyDepartureReason.None)
    throw new Exception("The first observed company must initialize history without a departure.");
history.m_PendingReason = CompanyDepartureReason.BankruptcyMissingInputs;
history = (SignatureCompanyHistory)observeMethod.Invoke(null, new object[] { history, second, CompanyDepartureReason.ExternalOrLoadReplacement });
if (history.m_CurrentCompany != second || history.m_LastReason != CompanyDepartureReason.BankruptcyMissingInputs || history.m_PendingReason != CompanyDepartureReason.None)
    throw new Exception("A recorded departure must move from pending to previous when the tenant changes.");
if ((string)reasonMethod.Invoke(null, new object[] { history.m_LastReason }) != "Bankruptcy: Missing materials")
    throw new Exception("The recorded departure reason must have a player-facing label.");
history = (SignatureCompanyHistory)observeMethod.Invoke(null, new object[] { history, third, CompanyDepartureReason.PropertyRelocation });
if (history.m_LastReason != CompanyDepartureReason.PropertyRelocation)
    throw new Exception("An observed rent/property unlink must classify the replacement as relocation.");
if ((string)reasonMethod.Invoke(null, new object[] { history.m_LastReason }) != "Relocated: Rent/property change")
    throw new Exception("The relocation reason must have a player-facing label.");

Console.WriteLine("Bankruptcy, affordability, starting-resource, balanced-input, full-load, and company-history checks passed (32/32).");
