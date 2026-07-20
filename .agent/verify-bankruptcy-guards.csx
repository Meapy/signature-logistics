using System;
using System.Reflection;

var type = typeof(SignatureFix.SignatureFixSystem);
var flags = BindingFlags.Static | BindingFlags.NonPublic;
var purchaseMethod = type.GetMethod("IsPriorityPurchaseSafe", flags);
var bankruptcyMethod = type.GetMethod(
    "IsMatureBankruptcy",
    flags,
    null,
    new[] { typeof(int), typeof(int), typeof(uint), typeof(uint) },
    null);
if (purchaseMethod == null || bankruptcyMethod == null)
    throw new Exception("Bankruptcy guard helper missing.");

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

Console.WriteLine("Bankruptcy and affordability boundary checks passed (8/8).");
