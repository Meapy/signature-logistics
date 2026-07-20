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
if (purchaseMethod == null || bankruptcyMethod == null || observeMethod == null || reasonMethod == null)
    throw new Exception("Compiled guard helper missing.");

bool Safe(int worth, int limit, float price, int amount) =>
    (bool)purchaseMethod.Invoke(null, new object[] { worth, limit, price, amount });

bool Bankrupt(int worth, int limit, uint since, uint frame) =>
    (bool)bankruptcyMethod.Invoke(null, new object[] { worth, limit, since, frame });

if (!Safe(1000, 500, 2f, 250)) throw new Exception("Purchase at the threshold should be allowed.");
if (Safe(999, 500, 2f, 250)) throw new Exception("Purchase below the threshold should be blocked.");
if (Safe(1000, 500, 2f, 0)) throw new Exception("Empty purchases should be blocked.");
if (!Safe(-100, -500, -5f, 1)) throw new Exception("Negative prices must not create a reserve.");

if (Bankrupt(500, 500, 1, 65538)) throw new Exception("A solvent company must not be bankrupt.");
if (Bankrupt(499, 500, 0, 65537)) throw new Exception("A company without a low-income timer must not be bankrupt.");
if (Bankrupt(499, 500, 1, 65537)) throw new Exception("The complete grace period must be preserved.");
if (!Bankrupt(499, 500, 1, 65538)) throw new Exception("A mature genuine bankruptcy must be allowed.");

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

Console.WriteLine("Bankruptcy, affordability, and company-history checks passed (13/13).");
