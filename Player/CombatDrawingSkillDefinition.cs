using UnityEngine;

public enum CombatDrawingEffectKind
{
    None = 0,
    CancellationLine = 1,
    ClockPin = 2,
    InkReveal = 3,
    RestoreLine = 5,
    AreaDamage = 6,
    HeartProjectile = 7,
    TimeSlow = 8,
    WorldPlacementTest = 9,
    InkSlash = 10,
    InkEruption = 11
}

public enum CombatDrawingSkillType
{
    Utility = 0,
    Area = 1,
    Projectile = 2,
    Summon = 3
}

[CreateAssetMenu(
    menuName = "Marginalia/Drawing/Combat Skill Definition",
    fileName = "CombatDrawingSkillDefinition")]

public sealed class CombatDrawingSkillDefinition : ScriptableObject
{
    [SerializeField] private string skillId = "combat-skill";
    [SerializeField] private string displayName = "Combat Skill";
    [SerializeField, TextArea] private string description;
    [SerializeField] private DrawingPatternDefinition pattern;
    [SerializeField] private CombatDrawingEffectKind effectKind;
    [SerializeField, Min(0.01f)] private float effectDuration = 3f;
    [SerializeField, Min(0)] private int effectPower;
    [SerializeField] private bool consumesCombatInk = true;
    [SerializeField] private Color effectColor = new Color(0.12f, 0.08f, 0.06f, 1f);
    [Header("Cast")]
    [SerializeField] private CombatDrawingSkillType skillType = CombatDrawingSkillType.Utility;
    [SerializeField, Min(0f)] private float range = 3f;
    [SerializeField, Min(0)] private int manaCost = 1;
    [SerializeField, Min(0)] private int damage;
    [SerializeField] private GameObject effectPrefab;
    [SerializeField, Min(0.01f)] private float spawnedEffectLifetime = 0.5f;
    [SerializeField, Min(0f)] private float cooldown;
    [Header("Time Slow")]
    [SerializeField, Range(0.01f, 1f)] private float worldTimeScale = 0.1f;
    [SerializeField, Min(1f)] private float playerDamageMultiplier = 1.2f;
    [Header("Impact Feedback")]
    [SerializeField, Min(0f)] private float impactFeedbackScale = 1f;
    [SerializeField, Min(0f)] private float impactHitstopDuration;

    public string SkillId => skillId;
    public string DisplayName => displayName;
    public string Description => description;
    public DrawingPatternDefinition Pattern => pattern;
    public CombatDrawingEffectKind EffectKind => effectKind;
    public float EffectDuration => effectDuration;
    public int EffectPower => effectPower;
    public bool ConsumesCombatInk => consumesCombatInk;
    public Color EffectColor => effectColor;
    public CombatDrawingSkillType SkillType => skillType;
    public float Range => range;
    public int ManaCost => manaCost;
    public int Damage => damage;
    public GameObject EffectPrefab => effectPrefab;
    public float SpawnedEffectLifetime => spawnedEffectLifetime;
    public float Cooldown => cooldown;
    public float WorldTimeScale => worldTimeScale;
    public float PlayerDamageMultiplier => playerDamageMultiplier;
    public float ImpactFeedbackScale => Mathf.Max(0f, impactFeedbackScale);
    public float ImpactHitstopDuration => Mathf.Max(0f, impactHitstopDuration);
    public bool IsAreaDamageSkill => effectKind == CombatDrawingEffectKind.AreaDamage;
    public bool IsHeartProjectileSkill => effectKind == CombatDrawingEffectKind.HeartProjectile;
    public bool IsTimeSlowSkill => effectKind == CombatDrawingEffectKind.TimeSlow;
    public bool IsWorldPlacementTestSkill => effectKind == CombatDrawingEffectKind.WorldPlacementTest;
    public bool IsInkSlashSkill => effectKind == CombatDrawingEffectKind.InkSlash;
    public bool IsInkEruptionSkill => effectKind == CombatDrawingEffectKind.InkEruption;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(skillId)
        && !string.IsNullOrWhiteSpace(displayName)
        && pattern != null
        && pattern.IsConfigured
        && effectKind != CombatDrawingEffectKind.None;

    private void OnValidate()
    {
        skillId = string.IsNullOrWhiteSpace(skillId) ? name : skillId.Trim();
        displayName = string.IsNullOrWhiteSpace(displayName) ? skillId : displayName.Trim();
        effectDuration = Mathf.Max(0.01f, effectDuration);
        effectPower = Mathf.Max(0, effectPower);
        effectColor.a = Mathf.Clamp01(effectColor.a);
        range = Mathf.Max(0f, range);
        manaCost = Mathf.Max(0, manaCost);
        damage = Mathf.Max(0, damage);
        spawnedEffectLifetime = Mathf.Max(0.01f, spawnedEffectLifetime);
        cooldown = Mathf.Max(0f, cooldown);
        worldTimeScale = Mathf.Clamp(worldTimeScale, 0.01f, 1f);
        playerDamageMultiplier = Mathf.Max(1f, playerDamageMultiplier);
        impactFeedbackScale = Mathf.Max(0f, impactFeedbackScale);
        impactHitstopDuration = Mathf.Max(0f, impactHitstopDuration);
    }
}
