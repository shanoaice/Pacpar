use std::env;
use std::error::Error;

fn main() -> Result<(), Box<dyn Error>> {
    // using bindgen, generate binding code
    #[allow(dead_code)]
    #[allow(unused_variables)]
    let lib = pkg_config::Config::new()
        .atleast_version("13.0.0")
        .probe("libalpm")
        .unwrap();

    let header = lib
        .include_paths
        .iter()
        .map(|i| i.join("alpm.h"))
        .find(|i| i.exists())
        .expect("could not find alpm.h");
    let mut include = lib
        .include_paths
        .iter()
        .map(|i| format!("-I{}", i.display().to_string()))
        .collect::<Vec<_>>();

    println!("cargo:rerun-if-env-changed=ALPM_INCLUDE_DIR");
    if let Ok(path) = env::var("ALPM_INCLUDE_DIR") {
        include.clear();
        include.insert(0, path);
    }

    let bindings = bindgen::builder()
        .clang_args(&include)
        .header(header.display().to_string())
        .allowlist_type("(alpm|ALPM).*")
        .allowlist_function("(alpm|ALPM).*")
        .rustified_enum("_alpm_[a-z_]+_t")
        .rustified_enum("alpm_download_event_type_t")
        .rustified_enum("_alpm_siglevel_t")
        .rustified_enum("_alpm_pkgvalidation_t")
        .rustified_enum("_alpm_loglevel_t")
        .rustified_enum("_alpm_question_type_t")
        .rustified_enum("_alpm_transflag_t")
        .rustified_enum("_alpm_db_usage_")
        .rustified_enum("_alpm_db_usage_t")
        .rustified_enum("alpm_caps")
        .opaque_type("alpm_handle_t")
        .opaque_type("alpm_db_t")
        .opaque_type("alpm_pkg_t")
        .opaque_type("alpm_trans_t")
        .size_t_is_usize(true)
        .derive_eq(true)
        .derive_ord(true)
        .derive_copy(true)
        .derive_hash(true)
        .derive_debug(true)
        .derive_partialeq(true)
        .derive_debug(true)
        .generate()
        .unwrap();

    bindings.write_to_file("alpm.rs").unwrap();

    // csbindgen code, generate C# dll import
    csbindgen::Builder::default()
        .input_bindgen_file("alpm.rs") // read from bindgen generated code
        .csharp_dll_name("libalpm")
        // .csharp_generate_const_filter(|x| x.starts_with("alpm_")) // use csharp_generate_const_filter if you want to generate const
        .csharp_class_name("NativeMethods")
        .csharp_class_accessibility("public")
        .csharp_namespace("Pacpar.Alpm")
        .generate_csharp_file("../NativeMethods.libalpm.g.cs")?;

    Ok(())
}
