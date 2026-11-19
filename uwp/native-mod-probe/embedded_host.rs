use horizon_mod_package::{
    dependencies::{self, Overrides, Resolution},
    runtime::Runtime,
};
use mod_api::__private::native::BuiltMod;

pub type HookFactory = fn() -> Box<dyn mod_api::Hooks>;

pub(crate) fn prepare(
    startup: &horizon_client::entry::Startup,
    built: &'static [BuiltMod],
    factories: &'static [HookFactory],
) -> horizon_client::Result<(Runtime, Resolution)> {
    if built.len() != factories.len() {
        return Err(
            std::io::Error::other("native hook inventory and factory counts differ").into(),
        );
    }
    if built.is_empty() {
        return Ok((
            Runtime::default(),
            Resolution {
                order: Vec::new(),
                warnings: Vec::new(),
            },
        ));
    }
    let exe = std::env::current_exe()?;
    let native = Runtime::load(&exe, built)?;
    let launch = startup.launch();
    let list = horizon_mod_package::mod_list::ModList::load(&launch.context.game_dir);
    let content: Vec<_> = horizon_mod_package::selection::content_packages(
        &launch.context.game_dir,
        &list.enabled,
        &launch.context.dev_mods,
    )
    .collect();
    let mut manifests: Vec<_> = native
        .packages()
        .iter()
        .map(|package| &package.manifest)
        .chain(content.iter().map(|source| &source.package.manifest))
        .collect();
    let side = launch.installation_side();
    let declared: Vec<_> = manifests[..built.len()]
        .iter()
        .map(|manifest| dependencies::from_manifest(&manifest.dependencies, side))
        .collect();
    let links: Vec<_> = manifests
        .iter()
        .zip(&declared)
        .map(|(manifest, dependencies)| dependencies::Mod {
            id: &manifest.id,
            version: &manifest.version,
            has_api: true,
            code: true,
            dependencies,
        })
        .collect();
    dependencies::resolve_links(&links)
        .map_err(|error| std::io::Error::other(error.to_string()))?;
    let overrides = Overrides::read(
        &launch
            .context
            .game_dir
            .join(horizon_mod_package::mod_list::DIR),
    )
    .map_err(std::io::Error::other)?;
    let unavailable = horizon_mod_package::unavailable::refusals(&manifests, side, &overrides);
    let content = manifests.split_off(built.len());
    manifests.extend(
        content
            .into_iter()
            .filter(|manifest| !unavailable.contains_key(&manifest.id)),
    );
    let order = dependencies::resolve_manifests(&manifests, side, &overrides)
        .map_err(|error| std::io::Error::other(error.to_string()))?;
    Ok((native, order))
}

pub(crate) fn install(
    built: &'static [BuiltMod],
    factories: &'static [HookFactory],
    order: Resolution,
) -> Vec<&'static str> {
    for warning in order.warnings {
        log::warn!("Horizon: {warning}");
    }
    let mut entries = Vec::with_capacity(built.len());
    let mut ids = Vec::with_capacity(built.len());
    let mut saved = Vec::with_capacity(built.len());
    for index in order.order.into_iter().filter(|&i| i < built.len()) {
        let entry = &built[index];
        ids.push(entry.id);
        entries.push(mod_api::ModEntry::new(entry.id, (factories[index])()));
        saved.push(horizon_client::save::level_mods::ModRecord {
            id: entry.id.to_string(),
            version: entry.version.to_string(),
        });
        log::info!("Embedded native mod: {} {}", entry.id, entry.version);
    }
    if !built.is_empty() {
        horizon_client::save::level_mods::set_loaded(Some(saved));
    }
    mod_api::__private::install(entries.into());
    ids
}
