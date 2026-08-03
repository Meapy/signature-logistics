using Game;
using UnityEngine.Scripting;

namespace SignatureFix
{
    /// <summary>
    /// Re-applies the scoped company prefabs that <see cref="SignatureFixSystem.PreSerialize"/> released so the city
    /// could be written with valid prefab references.
    ///
    /// <para>
    /// This runs in <see cref="SystemUpdatePhase.Serialize"/>, ordered after <c>WriteSystem</c>. Keeping both halves
    /// inside the same phase matters: no simulation frame runs between the release and the restore, so signature
    /// tenants are never exposed to the vanilla storage limit. That exposure is what destroys them -
    /// <c>ProcessingCompanySystem</c> produces a negative amount when stock exceeds the limit, and
    /// <c>IndustrialAISystem</c> sheds workers once stock passes half of it.
    /// </para>
    /// </summary>
    public partial class SignatureScopeRestoreSystem : GameSystemBase
    {
        private SignatureFixSystem m_SignatureFixSystem;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_SignatureFixSystem = World.GetOrCreateSystemManaged<SignatureFixSystem>();
        }

        [Preserve]
        protected override void OnUpdate()
        {
            m_SignatureFixSystem.RestoreScopedLimitsAfterSerialize();
        }

        [Preserve]
        public SignatureScopeRestoreSystem()
        {
        }
    }
}
