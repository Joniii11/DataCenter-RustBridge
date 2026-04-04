//! Proc macros for the Data Center modloader API.
//!
//! These attribute macros eliminate FFI boilerplate when writing Rust mods.
//! They generate `#[no_mangle] pub extern "C"` exports with automatic
//! `catch_unwind` panic handling, crash logging, and API lookup.
//!
//! # Available macros
//!
//! | Macro | Generates | User function signature |
//! |---|---|---|
//! | `#[mod_entry(...)]` | `mod_info()` + `mod_init()` | `fn(api: &Api) -> bool` |
//! | `#[on_update]` | `mod_update(f32)` | `fn(api: &Api, dt: f32)` |
//! | `#[on_event]` | `mod_on_event(u32, *const u8, u32)` | `fn(api: &Api, event: Event)` |
//! | `#[on_scene_loaded]` | `mod_on_scene_loaded(*const c_char)` | `fn(api: &Api, name: &str)` |
//! | `#[on_shutdown]` | `mod_shutdown()` | `fn(api: &Api)` |

use proc_macro::TokenStream;
use quote::{format_ident, quote};
use syn::parse::{Parse, ParseStream};
use syn::{parse_macro_input, ItemFn, LitStr, Token};

fn extract_param_name(arg: &syn::FnArg) -> Option<syn::Ident> {
    if let syn::FnArg::Typed(pat_type) = arg {
        if let syn::Pat::Ident(pat_ident) = pat_type.pat.as_ref() {
            return Some(pat_ident.ident.clone());
        }
    }
    None
}

struct ModEntryArgs {
    id: LitStr,
    name: LitStr,
    version: LitStr,
    author: LitStr,
    description: LitStr,
}

impl Parse for ModEntryArgs {
    fn parse(input: ParseStream) -> syn::Result<Self> {
        let mut id = None;
        let mut name = None;
        let mut version = None;
        let mut author = None;
        let mut description = None;

        while !input.is_empty() {
            let key: syn::Ident = input.parse()?;
            input.parse::<Token![=]>()?;
            let value: LitStr = input.parse()?;

            match key.to_string().as_str() {
                "id" => id = Some(value),
                "name" => name = Some(value),
                "version" => version = Some(value),
                "author" => author = Some(value),
                "description" => description = Some(value),
                other => {
                    return Err(syn::Error::new(
                        key.span(),
                        format!("unknown field `{}`", other),
                    ))
                }
            }

            // Allow trailing comma
            if !input.is_empty() {
                input.parse::<Token![,]>()?;
            }
        }

        Ok(Self {
            id: id.ok_or_else(|| input.error("missing required field `id`"))?,
            name: name.ok_or_else(|| input.error("missing required field `name`"))?,
            version: version.ok_or_else(|| input.error("missing required field `version`"))?,
            author: author.ok_or_else(|| input.error("missing required field `author`"))?,
            description: description
                .ok_or_else(|| input.error("missing required field `description`"))?,
        })
    }
}

/// Generates `mod_info()` and `mod_init()` FFI exports from a simple init function.
///
/// The decorated function must have the signature `fn(api: &Api) -> bool`.
/// Return `true` to indicate successful initialization, `false` to abort loading.
///
/// # Example
///
/// ```rust,ignore
/// #[dc_api::mod_entry(
///     id = "my_mod",
///     name = "My Mod",
///     version = "1.0.0",
///     author = "Author",
///     description = "A cool mod",
/// )]
/// fn init(api: &dc_api::Api) -> bool {
///     api.log_info("Hello from my mod!");
///     true
/// }
/// ```
///
/// This generates:
/// - `mod_info()` — returns mod metadata to the loader
/// - `mod_init(&'static GameAPI) -> bool` — sets up panic hook, crash log,
///   stores the API reference, then calls your init function
#[proc_macro_attribute]
pub fn mod_entry(attr: TokenStream, item: TokenStream) -> TokenStream {
    let args = parse_macro_input!(attr as ModEntryArgs);
    let input_fn = parse_macro_input!(item as ItemFn);

    let id = &args.id;
    let name = &args.name;
    let version = &args.version;
    let author = &args.author;
    let desc = &args.description;

    let user_fn_name = &input_fn.sig.ident;
    let internal_name = format_ident!("__dc_user_{}", user_fn_name);

    let mut internal_fn = input_fn.clone();
    internal_fn.sig.ident = internal_name.clone();
    internal_fn.vis = syn::Visibility::Inherited;

    let loaded_msg = format!("[{}] v{} loaded", name.value(), version.value());
    let api_ver_prefix = format!("[{}] API version: ", name.value());

    let expanded = quote! {
        #internal_fn

        #[no_mangle]
        pub extern "C" fn mod_info() -> ::dc_api::ModInfo {
            let __result = ::std::panic::catch_unwind(|| {
                ::dc_api::ModInfo::new(#id, #name, #version, #author, #desc)
            });
            match __result {
                Ok(__info) => __info,
                Err(__e) => {
                    ::dc_api::__internal_crash_log(&format!(
                        "[mod_info] panic: {}",
                        ::dc_api::__internal_panic_to_string(&__e)
                    ));
                    ::dc_api::ModInfo {
                        id: ::std::ptr::null(),
                        name: ::std::ptr::null(),
                        version: ::std::ptr::null(),
                        author: ::std::ptr::null(),
                        description: ::std::ptr::null(),
                    }
                }
            }
        }

        #[no_mangle]
        pub extern "C" fn mod_init(
            __game_api: &'static ::dc_api::GameAPI,
        ) -> bool {
            ::dc_api::__internal_setup_panic_hook();
            ::dc_api::__internal_set_crash_log(&format!("dc_{}_crash.log", #id));
            ::dc_api::__internal_crash_log("[mod_init] >>> enter");

            let __result = ::std::panic::catch_unwind(|| {
                let __api = unsafe { ::dc_api::Api::from_raw(__game_api) };
                __api.log_info(#loaded_msg);
                __api.log_info(&format!("{}{}", #api_ver_prefix, __api.version()));

                ::dc_api::__internal_set_mod_api(__api);

                if let Some(__api_ref) = ::dc_api::__internal_mod_api() {
                    #internal_name(__api_ref)
                } else {
                    ::dc_api::__internal_crash_log("[mod_init] API storage failed");
                    false
                }
            });

            match __result {
                Ok(__v) => {
                    ::dc_api::__internal_crash_log("[mod_init] <<< exit");
                    __v
                }
                Err(__e) => {
                    ::dc_api::__internal_crash_log(&format!(
                        "[mod_init] panic: {}",
                        ::dc_api::__internal_panic_to_string(&__e)
                    ));
                    false
                }
            }
        }
    };

    expanded.into()
}

/// Generates a `mod_update(f32)` FFI export with automatic panic handling.
///
/// The decorated function must have the signature `fn(api: &Api, dt: f32)`.
///
/// # Example
///
/// ```rust,ignore
/// #[dc_api::on_update]
/// fn update(api: &dc_api::Api, dt: f32) {
///     // called every frame
/// }
/// ```
#[proc_macro_attribute]
pub fn on_update(_attr: TokenStream, item: TokenStream) -> TokenStream {
    let input_fn = parse_macro_input!(item as ItemFn);

    let user_fn_name = &input_fn.sig.ident;
    let internal_name = format_ident!("__dc_user_{}", user_fn_name);

    let mut internal_fn = input_fn.clone();
    internal_fn.sig.ident = internal_name.clone();
    internal_fn.vis = syn::Visibility::Inherited;

    // Extract user's parameter names so the body compiles
    let params: Vec<_> = input_fn.sig.inputs.iter().collect();
    let api_name = params
        .first()
        .and_then(|a| extract_param_name(a))
        .unwrap_or_else(|| format_ident!("api"));
    let dt_name = params
        .get(1)
        .and_then(|a| extract_param_name(a))
        .unwrap_or_else(|| format_ident!("dt"));

    let body = &input_fn.block;
    let attrs = &input_fn.attrs;

    let expanded = quote! {
        #(#attrs)*
        fn #internal_name(#api_name: &::dc_api::Api, #dt_name: f32) #body

        #[no_mangle]
        pub extern "C" fn mod_update(__dt: f32) {
            let __result =
                ::std::panic::catch_unwind(::std::panic::AssertUnwindSafe(|| {
                    if let Some(__api) = ::dc_api::__internal_mod_api() {
                        #internal_name(__api, __dt);
                    }
                }));
            if let Err(__e) = __result {
                ::dc_api::__internal_crash_log(&format!(
                    "[mod_update] panic: {}",
                    ::dc_api::__internal_panic_to_string(&__e)
                ));
            }
        }
    };

    expanded.into()
}

/// Generates a `mod_on_event(u32, *const u8, u32)` FFI export with automatic
/// event decoding and panic handling.
///
/// The decorated function receives an already-decoded `Event` enum.
/// The signature must be `fn(api: &Api, event: Event)`.
///
/// # Example
///
/// ```rust,ignore
/// #[dc_api::on_event]
/// fn handle(api: &dc_api::Api, event: dc_api::Event) {
///     match event {
///         dc_api::Event::DayEnded { day } => {
///             api.log_info(&format!("Day {} ended", day));
///         }
///         _ => {}
///     }
/// }
/// ```
#[proc_macro_attribute]
pub fn on_event(_attr: TokenStream, item: TokenStream) -> TokenStream {
    let input_fn = parse_macro_input!(item as ItemFn);

    let user_fn_name = &input_fn.sig.ident;
    let internal_name = format_ident!("__dc_user_{}", user_fn_name);

    let params: Vec<_> = input_fn.sig.inputs.iter().collect();
    let api_name = params
        .first()
        .and_then(|a| extract_param_name(a))
        .unwrap_or_else(|| format_ident!("api"));
    let event_name = params
        .get(1)
        .and_then(|a| extract_param_name(a))
        .unwrap_or_else(|| format_ident!("event"));

    let body = &input_fn.block;
    let attrs = &input_fn.attrs;

    let expanded = quote! {
        #(#attrs)*
        fn #internal_name(#api_name: &::dc_api::Api, #event_name: ::dc_api::Event) #body

        #[no_mangle]
        pub extern "C" fn mod_on_event(
            __event_id: u32,
            __event_data: *const u8,
            __data_size: u32,
        ) {
            let __result =
                ::std::panic::catch_unwind(::std::panic::AssertUnwindSafe(|| {
                    let Some(__event) = ::dc_api::events::decode(
                        __event_id,
                        __event_data,
                        __data_size,
                    ) else {
                        return;
                    };
                    if let Some(__api) = ::dc_api::__internal_mod_api() {
                        #internal_name(__api, __event);
                    }
                }));
            if let Err(__e) = __result {
                ::dc_api::__internal_crash_log(&format!(
                    "[mod_on_event] panic (event_id={}): {}",
                    __event_id,
                    ::dc_api::__internal_panic_to_string(&__e)
                ));
            }
        }
    };

    expanded.into()
}

/// Generates a `mod_on_scene_loaded(*const c_char)` FFI export with automatic
/// C-string conversion and panic handling.
///
/// The decorated function receives a `&str` scene name.
/// The signature must be `fn(api: &Api, name: &str)`.
///
/// # Example
///
/// ```rust,ignore
/// #[dc_api::on_scene_loaded]
/// fn scene(api: &dc_api::Api, name: &str) {
///     api.log_info(&format!("Loaded scene: {}", name));
/// }
/// ```
#[proc_macro_attribute]
pub fn on_scene_loaded(_attr: TokenStream, item: TokenStream) -> TokenStream {
    let input_fn = parse_macro_input!(item as ItemFn);

    let user_fn_name = &input_fn.sig.ident;
    let internal_name = format_ident!("__dc_user_{}", user_fn_name);

    let params: Vec<_> = input_fn.sig.inputs.iter().collect();
    let api_name = params
        .first()
        .and_then(|a| extract_param_name(a))
        .unwrap_or_else(|| format_ident!("api"));
    let name_param = params
        .get(1)
        .and_then(|a| extract_param_name(a))
        .unwrap_or_else(|| format_ident!("scene_name"));

    let body = &input_fn.block;
    let attrs = &input_fn.attrs;

    let expanded = quote! {
        #(#attrs)*
        fn #internal_name(#api_name: &::dc_api::Api, #name_param: &str) #body

        #[no_mangle]
        pub extern "C" fn mod_on_scene_loaded(
            __scene_ptr: *const ::std::ffi::c_char,
        ) {
            let __result =
                ::std::panic::catch_unwind(::std::panic::AssertUnwindSafe(|| {
                    if __scene_ptr.is_null() {
                        return;
                    }
                    let __cow = unsafe {
                        ::std::ffi::CStr::from_ptr(__scene_ptr)
                    }
                    .to_string_lossy();
                    let __name: &str = &__cow;
                    if let Some(__api) = ::dc_api::__internal_mod_api() {
                        #internal_name(__api, __name);
                    }
                }));
            if let Err(__e) = __result {
                ::dc_api::__internal_crash_log(&format!(
                    "[mod_on_scene_loaded] panic: {}",
                    ::dc_api::__internal_panic_to_string(&__e)
                ));
            }
        }
    };

    expanded.into()
}

/// Generates a `mod_shutdown()` FFI export with automatic panic handling.
///
/// The decorated function must have the signature `fn(api: &Api)`.
///
/// # Example
///
/// ```rust,ignore
/// #[dc_api::on_shutdown]
/// fn shutdown(api: &dc_api::Api) {
///     api.log_info("Goodbye!");
/// }
/// ```
#[proc_macro_attribute]
pub fn on_shutdown(_attr: TokenStream, item: TokenStream) -> TokenStream {
    let input_fn = parse_macro_input!(item as ItemFn);

    let user_fn_name = &input_fn.sig.ident;
    let internal_name = format_ident!("__dc_user_{}", user_fn_name);

    let params: Vec<_> = input_fn.sig.inputs.iter().collect();
    let api_name = params
        .first()
        .and_then(|a| extract_param_name(a))
        .unwrap_or_else(|| format_ident!("api"));

    let body = &input_fn.block;
    let attrs = &input_fn.attrs;

    let expanded = quote! {
        #(#attrs)*
        fn #internal_name(#api_name: &::dc_api::Api) #body

        #[no_mangle]
        pub extern "C" fn mod_shutdown() {
            let __result =
                ::std::panic::catch_unwind(::std::panic::AssertUnwindSafe(|| {
                    if let Some(__api) = ::dc_api::__internal_mod_api() {
                        #internal_name(__api);
                    } else {
                        ::dc_api::__internal_crash_log(
                            "[mod_shutdown] API not initialised",
                        );
                    }
                }));
            if let Err(__e) = __result {
                ::dc_api::__internal_crash_log(&format!(
                    "[mod_shutdown] panic: {}",
                    ::dc_api::__internal_panic_to_string(&__e)
                ));
            }
        }
    };

    expanded.into()
}
