namespace PharmaX.Persistence.Seeding;

/// <summary>
/// The medical aid societies trading in Zimbabwe, as a pharmacy would
/// bill them.
/// </summary>
/// <remarks>
/// Taken from the member list of the Association of Healthcare Funders
/// of Zimbabwe (ahfoz.org/our-members, September 2026), keeping the
/// funders and leaving out its associate members that do not pay claims
/// (hospital groups, a pharmaceutical manufacturer, a bank, a
/// micro-insurer, NSSA, and the regional Eswatini fund). Cimas, Fidelity
/// Life Medical Aid Society and Maisha Health Fund are added: they
/// trade and are billed every day, although AHFoZ does not list them.
///
/// Seeded by code and only where the code is missing, so a society the
/// pharmacy has renamed or retired stays as the pharmacy left it. The
/// list changes - societies merge and new ones register - and the
/// Medical Aid Societies screen is where it is kept up to date.
/// </remarks>
public static class ZimbabweMedicalAidSocieties
{
    public static IReadOnlyList<(string Code, string Name, string Detail)> All { get; } = new[]
    {
        ("CIMAS", "Cimas Medical Aid Society", "Cimas Health Group, cimas.co.zw"),
        ("PSMAS", "Premier Service Medical Aid Society (PSMAS)", "psmas.co.zw"),
        ("FMH", "First Mutual Health", "First Mutual Holdings"),
        ("FLIMAS", "Fidelity Life Medical Aid Society (FLIMAS)", "Fidelity Life Assurance, fidelitylife.co.zw"),
        ("ALLIANCE", "Alliance Health (Northern Medical Aid)", "alliancehealth.co.zw"),
        ("MASCA", "MASCA Medical Aid Society", "Medical Aid Society of Central Africa"),
        ("BONVIE", "BonVie Medical Aid Scheme", "AHFoZ member"),
        ("CELLMED", "Cellmed Health Fund", "AHFoZ member"),
        ("EMF", "EMF Medical Aid", "AHFoZ member"),
        ("GENERATION", "Generation Health", "AHFoZ member"),
        ("MAISHA", "Maisha Health Fund", "EcoCash Holdings, maishahealthfund.co.zw"),
        ("PROHEALTH", "ProHealth Medical Aid Society", "AHFoZ member"),
        ("FAMAS", "For All Medical Aid Society (FA-MAS)", "AHFoZ member"),
        ("SAVELIFE", "Savelife Medical Aid", "AHFoZ member"),
        ("CLIENTSURE", "Clientsure", "AHFoZ member"),
        ("ULTRAMED", "Ultramed Health", "AHFoZ member"),
        ("SALUTEM", "Salutem International Medical Fund", "AHFoZ member"),
        ("PARKSMEAD", "Parksmead Health Fund", "AHFoZ member"),
        ("CHIMAS", "Calm Health International Medical Aid Society (CHIMAS)", "AHFoZ member"),
        ("HMMAS", "HMMAS", "AHFoZ member"),
        ("QUESTVITALITY", "Quest Vitality Medical", "AHFoZ member"),
        ("BUDGETHEALTH", "Budget Health Medical Aid Society", "AHFoZ member"),
        ("NEWHEALTH263", "NewHealth263", "AHFoZ member"),
    };
}
