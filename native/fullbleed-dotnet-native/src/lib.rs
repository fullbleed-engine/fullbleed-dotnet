#![deny(unsafe_op_in_unsafe_fn)]

use base64::Engine as _;
use fullbleed::{
    Asset, AssetBundle, AssetKind, Color, ColorSpace, CompiledDocument, CompiledFlowCompression,
    CompiledReflowOptions, ComposeAnnotationMode, ComposePagePlan, FullBleed, FullBleedError,
    JitMode, LayoutStrategy, Margins, OutputIntent, PageDataContext, PageDataValue,
    PaginatedContextSpec, PdfInspectError, PdfProfile, PdfVersion, Pt, Size, TemplateAsset,
    TemplateBindingSpec, TemplateCatalog, WatermarkKind, WatermarkLayer, WatermarkSemantics,
    WatermarkSpec, compose_overlay_with_template_catalog_with_annotation_mode,
    composition_compatibility_issues, inspect_pdf_path, stamp_overlay_on_template_pdf,
};
use serde::Deserialize;
use serde_json::{Value, json};
use std::collections::{BTreeMap, HashMap};
use std::ffi::{CString, c_char, c_void};
use std::os::raw::c_uchar;
use std::panic::{AssertUnwindSafe, catch_unwind};
use std::path::{Path, PathBuf};
use std::ptr;

const ABI_VERSION: u32 = 1;

#[repr(C)]
#[derive(Debug, Copy, Clone, PartialEq, Eq)]
pub enum FbStatusCode {
    Ok = 0,
    NullArgument = 1,
    InvalidUtf8 = 2,
    InvalidOptions = 3,
    RenderFailed = 4,
    IoFailed = 5,
    InvalidHandle = 6,
    SerializationFailed = 7,
    Panic = 255,
}

#[repr(C)]
#[derive(Debug, Copy, Clone)]
pub struct FbRenderOptions {
    pub page_width_pt: f32,
    pub page_height_pt: f32,
    pub margin_top_pt: f32,
    pub margin_right_pt: f32,
    pub margin_bottom_pt: f32,
    pub margin_left_pt: f32,
}

impl Default for FbRenderOptions {
    fn default() -> Self {
        Self {
            page_width_pt: 595.28,
            page_height_pt: 841.89,
            margin_top_pt: 36.0,
            margin_right_pt: 36.0,
            margin_bottom_pt: 36.0,
            margin_left_pt: 36.0,
        }
    }
}

#[repr(C)]
#[derive(Debug, Copy, Clone)]
pub struct FbByteBuffer {
    pub ptr: *mut c_uchar,
    pub len: usize,
}

impl FbByteBuffer {
    const fn empty() -> Self {
        Self {
            ptr: ptr::null_mut(),
            len: 0,
        }
    }
}

struct EngineHandle {
    engine: FullBleed,
}

struct CompiledHandle {
    document: CompiledDocument,
}

type FbCallError = (FbStatusCode, String);
type FbCallResult<T> = Result<T, FbCallError>;

#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
struct SizeOptions {
    width_pt: f32,
    height_pt: f32,
}

#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
struct MarginsOptions {
    top_pt: f32,
    right_pt: f32,
    bottom_pt: f32,
    left_pt: f32,
}

impl MarginsOptions {
    fn to_native(&self) -> FbCallResult<Margins> {
        for (name, value) in [
            ("topPt", self.top_pt),
            ("rightPt", self.right_pt),
            ("bottomPt", self.bottom_pt),
            ("leftPt", self.left_pt),
        ] {
            if !value.is_finite() || value < 0.0 {
                return Err(call_error(
                    FbStatusCode::InvalidOptions,
                    format!("margins.{name} must be finite and >= 0"),
                ));
            }
        }
        Ok(Margins {
            top: Pt::from_f32(self.top_pt),
            right: Pt::from_f32(self.right_pt),
            bottom: Pt::from_f32(self.bottom_pt),
            left: Pt::from_f32(self.left_pt),
        })
    }
}

#[derive(Debug, Clone, Copy, Deserialize)]
#[serde(rename_all = "camelCase")]
struct ColorOptions {
    r: f32,
    g: f32,
    b: f32,
}

impl ColorOptions {
    fn to_native(self) -> FbCallResult<Color> {
        if [self.r, self.g, self.b]
            .iter()
            .any(|value| !value.is_finite() || !(0.0..=1.0).contains(value))
        {
            return Err(call_error(
                FbStatusCode::InvalidOptions,
                "color channels must be finite values in the range 0..=1",
            ));
        }
        Ok(Color::rgb(self.r, self.g, self.b))
    }
}

#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
struct TextDecorationOptions {
    first: Option<String>,
    each: Option<String>,
    last: Option<String>,
    #[serde(default)]
    x_pt: f32,
    #[serde(default = "default_decoration_y")]
    y_pt: f32,
    #[serde(default = "default_font_name")]
    font_name: String,
    #[serde(default = "default_font_size")]
    font_size_pt: f32,
    #[serde(default = "default_black")]
    color: ColorOptions,
}

fn default_decoration_y() -> f32 {
    18.0
}

fn default_font_name() -> String {
    "Helvetica".to_string()
}

fn default_font_size() -> f32 {
    9.0
}

fn default_black() -> ColorOptions {
    ColorOptions {
        r: 0.0,
        g: 0.0,
        b: 0.0,
    }
}

#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
struct HtmlDecorationOptions {
    first: Option<String>,
    each: Option<String>,
    last: Option<String>,
    #[serde(default)]
    x_pt: f32,
    #[serde(default = "default_decoration_y")]
    y_pt: f32,
    width_pt: f32,
    height_pt: f32,
}

#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
struct WatermarkOptions {
    kind: String,
    value: String,
    #[serde(default = "default_watermark_layer")]
    layer: String,
    #[serde(default = "default_watermark_semantics")]
    semantics: String,
    #[serde(default = "default_watermark_opacity")]
    opacity: f32,
    #[serde(default)]
    rotation_deg: f32,
    #[serde(default = "default_watermark_font_size")]
    font_size_pt: f32,
    #[serde(default = "default_font_name")]
    font_name: String,
    #[serde(default = "default_watermark_color")]
    color: ColorOptions,
}

fn default_watermark_layer() -> String {
    "overlay".to_string()
}

fn default_watermark_semantics() -> String {
    "artifact".to_string()
}

fn default_watermark_opacity() -> f32 {
    0.15
}

fn default_watermark_font_size() -> f32 {
    48.0
}

fn default_watermark_color() -> ColorOptions {
    ColorOptions {
        r: 0.6,
        g: 0.6,
        b: 0.6,
    }
}

#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
struct OutputIntentOptions {
    icc_profile_path: Option<String>,
    icc_profile_base64: Option<String>,
    components: u8,
    identifier: String,
    info: Option<String>,
}

#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
struct AssetOptions {
    name: Option<String>,
    kind: String,
    path: Option<String>,
    data_base64: Option<String>,
    source: Option<String>,
    #[serde(default)]
    trusted: bool,
}

#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
struct TemplateBindingOptions {
    default_template_id: Option<String>,
    #[serde(default)]
    by_page_template: BTreeMap<String, String>,
    #[serde(default)]
    by_feature: BTreeMap<String, String>,
    #[serde(default = "default_feature_prefix")]
    feature_prefix: String,
}

fn default_feature_prefix() -> String {
    "fb.feature.".to_string()
}

#[derive(Debug, Default, Deserialize)]
#[serde(default, rename_all = "camelCase")]
struct EngineOptions {
    page_size: Option<SizeOptions>,
    margins: Option<MarginsOptions>,
    page_margins: BTreeMap<usize, MarginsOptions>,
    font_directories: Vec<String>,
    font_files: Vec<String>,
    reuse_xobjects: Option<bool>,
    svg_form_xobjects: Option<bool>,
    svg_raster_fallback: Option<bool>,
    unicode_support: Option<bool>,
    shape_text: Option<bool>,
    unicode_metrics: Option<bool>,
    pdf_version: Option<String>,
    pdf_profile: Option<String>,
    color_space: Option<String>,
    output_intent: Option<OutputIntentOptions>,
    document_language: Option<String>,
    document_title: Option<String>,
    jit_mode: Option<String>,
    layout_strategy: Option<String>,
    accept_lazy_layout_cost: bool,
    lazy_max_passes: Option<usize>,
    lazy_budget_ms: Option<f64>,
    debug_log_path: Option<String>,
    perf_log_path: Option<String>,
    perf_enabled: Option<bool>,
    header: Option<TextDecorationOptions>,
    header_html: Option<HtmlDecorationOptions>,
    footer: Option<TextDecorationOptions>,
    watermark: Option<WatermarkOptions>,
    paginated_context: BTreeMap<String, String>,
    template_binding: Option<TemplateBindingOptions>,
    assets: Vec<AssetOptions>,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct RenderJob {
    html: String,
    #[serde(default)]
    css: String,
}

#[derive(Debug, Default, Deserialize)]
#[serde(default, rename_all = "camelCase")]
struct BatchRequest {
    jobs: Vec<RenderJob>,
    parallel: bool,
    include_page_data: bool,
}

#[derive(Debug, Default, Deserialize)]
#[serde(default, rename_all = "camelCase")]
struct StampRequest {
    page_map: Option<Vec<[usize; 2]>>,
    dx: f32,
    dy: f32,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct ComposeTemplateInput {
    template_id: String,
    pdf_path: String,
    sha256: Option<String>,
    page_count: Option<usize>,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct ComposePlanInput {
    template_id: String,
    template_page_index: usize,
    overlay_page_index: usize,
    #[serde(default)]
    dx: f32,
    #[serde(default)]
    dy: f32,
}

#[derive(Debug, Default, Deserialize)]
#[serde(default, rename_all = "camelCase")]
struct ComposeRequest {
    templates: Vec<ComposeTemplateInput>,
    plan: Vec<ComposePlanInput>,
    annotation_mode: Option<String>,
}

fn call_error(code: FbStatusCode, message: impl Into<String>) -> FbCallError {
    (code, message.into())
}

fn map_engine_error(error: FullBleedError) -> FbCallError {
    match error {
        FullBleedError::InvalidConfiguration(message) => {
            call_error(FbStatusCode::InvalidOptions, message)
        }
        FullBleedError::Io(error) => call_error(FbStatusCode::IoFailed, error.to_string()),
        other => call_error(FbStatusCode::RenderFailed, other.to_string()),
    }
}

fn map_inspect_error(error: PdfInspectError) -> FbCallError {
    call_error(FbStatusCode::RenderFailed, error.to_string())
}

unsafe fn read_input<'a>(
    pointer: *const c_uchar,
    length: usize,
    name: &str,
) -> FbCallResult<&'a [u8]> {
    if length == 0 {
        return Ok(&[]);
    }
    if pointer.is_null() {
        return Err(call_error(
            FbStatusCode::NullArgument,
            format!("{name} pointer is null with non-zero length"),
        ));
    }
    // SAFETY: the caller supplies an immutable pointer/length pair for this call.
    Ok(unsafe { std::slice::from_raw_parts(pointer, length) })
}

unsafe fn read_utf8<'a>(
    pointer: *const c_uchar,
    length: usize,
    name: &str,
) -> FbCallResult<&'a str> {
    // SAFETY: delegated to read_input, which validates the pointer/length pair.
    let bytes = unsafe { read_input(pointer, length, name) }?;
    std::str::from_utf8(bytes).map_err(|error| {
        call_error(
            FbStatusCode::InvalidUtf8,
            format!("{name} is not valid UTF-8: {error}"),
        )
    })
}

unsafe fn parse_json<T: for<'de> Deserialize<'de>>(
    pointer: *const c_uchar,
    length: usize,
    name: &str,
) -> FbCallResult<T> {
    // SAFETY: delegated to read_input, which validates the pointer/length pair.
    let bytes = unsafe { read_input(pointer, length, name) }?;
    let bytes = if bytes.is_empty() { b"{}" } else { bytes };
    serde_json::from_slice(bytes).map_err(|error| {
        call_error(
            FbStatusCode::InvalidOptions,
            format!("invalid {name} JSON: {error}"),
        )
    })
}

fn write_error(out_error_message: *mut *mut c_char, message: &str) {
    if out_error_message.is_null() {
        return;
    }
    let sanitized = message.replace('\0', " ");
    if let Ok(message) = CString::new(sanitized) {
        // SAFETY: the out pointer was checked above and belongs to the caller.
        unsafe { *out_error_message = message.into_raw() };
    }
}

unsafe fn initialize_error(out_error_message: *mut *mut c_char) -> FbCallResult<()> {
    if out_error_message.is_null() {
        return Err(call_error(
            FbStatusCode::NullArgument,
            "out_error_message cannot be null",
        ));
    }
    // SAFETY: validated non-null above.
    unsafe { *out_error_message = ptr::null_mut() };
    Ok(())
}

unsafe fn initialize_buffer(buffer: *mut FbByteBuffer, name: &str) -> FbCallResult<()> {
    if buffer.is_null() {
        return Err(call_error(
            FbStatusCode::NullArgument,
            format!("{name} cannot be null"),
        ));
    }
    // SAFETY: validated non-null above.
    unsafe { *buffer = FbByteBuffer::empty() };
    Ok(())
}

unsafe fn write_buffer(out: *mut FbByteBuffer, bytes: Vec<u8>) {
    if bytes.is_empty() {
        // SAFETY: callers validate output pointers before invoking this helper.
        unsafe { *out = FbByteBuffer::empty() };
        return;
    }
    let boxed = bytes.into_boxed_slice();
    let len = boxed.len();
    let pointer = Box::into_raw(boxed) as *mut c_uchar;
    // SAFETY: callers validate output pointers before invoking this helper.
    unsafe {
        (*out).ptr = pointer;
        (*out).len = len;
    }
}

unsafe fn write_json(out: *mut FbByteBuffer, value: &Value) -> FbCallResult<()> {
    let bytes = serde_json::to_vec(value).map_err(|error| {
        call_error(
            FbStatusCode::SerializationFailed,
            format!("could not serialize native result: {error}"),
        )
    })?;
    // SAFETY: callers validate output pointers before invoking this helper.
    unsafe { write_buffer(out, bytes) };
    Ok(())
}

fn finish_call(
    out_error_message: *mut *mut c_char,
    call: impl FnOnce() -> FbCallResult<()>,
) -> FbStatusCode {
    match catch_unwind(AssertUnwindSafe(call)) {
        Ok(Ok(())) => FbStatusCode::Ok,
        Ok(Err((code, message))) => {
            write_error(out_error_message, &message);
            code
        }
        Err(_) => {
            write_error(
                out_error_message,
                "panic inside Fullbleed's native .NET bridge",
            );
            FbStatusCode::Panic
        }
    }
}

unsafe fn engine_from_handle<'a>(handle: *mut c_void) -> FbCallResult<&'a FullBleed> {
    if handle.is_null() {
        return Err(call_error(
            FbStatusCode::InvalidHandle,
            "Fullbleed engine handle is null",
        ));
    }
    // SAFETY: managed SafeHandle passes only pointers allocated by fullbleed_engine_create.
    Ok(&unsafe { &*(handle.cast::<EngineHandle>()) }.engine)
}

unsafe fn compiled_from_handle<'a>(handle: *mut c_void) -> FbCallResult<&'a CompiledDocument> {
    if handle.is_null() {
        return Err(call_error(
            FbStatusCode::InvalidHandle,
            "compiled document handle is null",
        ));
    }
    // SAFETY: managed SafeHandle passes only pointers allocated by fullbleed_engine_compile.
    Ok(&unsafe { &*(handle.cast::<CompiledHandle>()) }.document)
}

fn normalized(value: &str) -> String {
    value
        .chars()
        .filter(|character| character.is_ascii_alphanumeric())
        .flat_map(char::to_lowercase)
        .collect()
}

fn parse_pdf_version(value: &str) -> FbCallResult<PdfVersion> {
    match normalized(value).as_str() {
        "17" | "pdf17" => Ok(PdfVersion::Pdf17),
        "20" | "pdf20" => Ok(PdfVersion::Pdf20),
        _ => Err(call_error(
            FbStatusCode::InvalidOptions,
            format!("unsupported PDF version {value:?}; expected 1.7 or 2.0"),
        )),
    }
}

fn parse_pdf_profile(value: &str) -> FbCallResult<PdfProfile> {
    match normalized(value).as_str() {
        "none" => Ok(PdfProfile::None),
        "pdfa1a" => Ok(PdfProfile::PdfA1a),
        "pdfa1b" => Ok(PdfProfile::PdfA1b),
        "pdfa2a" => Ok(PdfProfile::PdfA2a),
        "pdfa2b" | "pdfa" | "a" => Ok(PdfProfile::PdfA2b),
        "pdfa2u" => Ok(PdfProfile::PdfA2u),
        "pdfa3a" => Ok(PdfProfile::PdfA3a),
        "pdfa3b" => Ok(PdfProfile::PdfA3b),
        "pdfa3u" => Ok(PdfProfile::PdfA3u),
        "pdfa4" => Ok(PdfProfile::PdfA4),
        "pdfa4e" => Ok(PdfProfile::PdfA4e),
        "pdfa4f" => Ok(PdfProfile::PdfA4f),
        "pdfx4" => Ok(PdfProfile::PdfX4),
        "pdfua1" | "pdfua" | "ua" => Ok(PdfProfile::PdfUa1),
        "pdfua2" => Ok(PdfProfile::PdfUa2),
        "pdfvt1" | "pdfvt" | "vt" => Ok(PdfProfile::PdfVt1),
        "wtpdf1r" | "wt1r" => Ok(PdfProfile::Wtpdf1r),
        "wtpdf1a" | "wt1a" => Ok(PdfProfile::Wtpdf1a),
        "tagged" => Ok(PdfProfile::Tagged),
        _ => Err(call_error(
            FbStatusCode::InvalidOptions,
            format!("unsupported PDF profile {value:?}"),
        )),
    }
}

fn parse_jit_mode(value: &str) -> FbCallResult<JitMode> {
    match normalized(value).as_str() {
        "off" => Ok(JitMode::Off),
        "planonly" => Ok(JitMode::PlanOnly),
        "planandreplay" => Ok(JitMode::PlanAndReplay),
        _ => Err(call_error(
            FbStatusCode::InvalidOptions,
            format!("unsupported JIT mode {value:?}"),
        )),
    }
}

fn parse_compression(value: &str) -> FbCallResult<CompiledFlowCompression> {
    match normalized(value).as_str() {
        "throughput" => Ok(CompiledFlowCompression::Throughput),
        "compact" => Ok(CompiledFlowCompression::Compact),
        _ => Err(call_error(
            FbStatusCode::InvalidOptions,
            format!("unsupported compiled-flow compression {value:?}"),
        )),
    }
}

fn parse_annotation_mode(value: Option<&str>) -> FbCallResult<ComposeAnnotationMode> {
    match value.map(normalized).as_deref() {
        None | Some("linkonly") => Ok(ComposeAnnotationMode::LinkOnly),
        Some("none") => Ok(ComposeAnnotationMode::None),
        Some("carrywidgets") => Ok(ComposeAnnotationMode::CarryWidgets),
        Some(_) => Err(call_error(
            FbStatusCode::InvalidOptions,
            "annotationMode must be none, linkOnly, or carryWidgets",
        )),
    }
}

fn asset_from_options(options: AssetOptions) -> FbCallResult<Asset> {
    let kind = AssetKind::from_str(&options.kind).ok_or_else(|| {
        call_error(
            FbStatusCode::InvalidOptions,
            format!("unsupported asset kind {:?}", options.kind),
        )
    })?;
    let data = match (options.path.as_deref(), options.data_base64.as_deref()) {
        (Some(_), Some(_)) => {
            return Err(call_error(
                FbStatusCode::InvalidOptions,
                "an asset cannot set both path and dataBase64",
            ));
        }
        (Some(path), None) => std::fs::read(path).map_err(|error| {
            call_error(
                FbStatusCode::IoFailed,
                format!("could not read asset {path:?}: {error}"),
            )
        })?,
        (None, Some(encoded)) => base64::engine::general_purpose::STANDARD
            .decode(encoded)
            .map_err(|error| {
                call_error(
                    FbStatusCode::InvalidOptions,
                    format!("asset dataBase64 is invalid: {error}"),
                )
            })?,
        (None, None) => {
            return Err(call_error(
                FbStatusCode::InvalidOptions,
                "an asset must set path or dataBase64",
            ));
        }
    };
    let name = options.name.unwrap_or_else(|| {
        options
            .path
            .as_deref()
            .and_then(|path| Path::new(path).file_name())
            .and_then(|name| name.to_str())
            .unwrap_or("asset")
            .to_string()
    });
    let source = options.source.or(options.path);
    Ok(Asset::new(name, kind, data, source, options.trusted))
}

fn build_engine(options: EngineOptions) -> FbCallResult<FullBleed> {
    let mut builder = FullBleed::builder();

    if let Some(size) = options.page_size {
        if !size.width_pt.is_finite()
            || size.width_pt <= 0.0
            || !size.height_pt.is_finite()
            || size.height_pt <= 0.0
        {
            return Err(call_error(
                FbStatusCode::InvalidOptions,
                "pageSize dimensions must be finite and > 0",
            ));
        }
        builder = builder.page_size(Size {
            width: Pt::from_f32(size.width_pt),
            height: Pt::from_f32(size.height_pt),
        });
    }
    if let Some(margins) = options.margins {
        builder = builder.margins(margins.to_native()?);
    }
    for (page, margins) in options.page_margins {
        if page == 0 {
            return Err(call_error(
                FbStatusCode::InvalidOptions,
                "pageMargins keys are 1-based and must be >= 1",
            ));
        }
        builder = builder.page_margin(page, margins.to_native()?);
    }
    for path in options.font_directories {
        builder = builder.register_font_dir(path);
    }
    for path in options.font_files {
        builder = builder.register_font_file(path);
    }
    if let Some(value) = options.reuse_xobjects {
        builder = builder.reuse_xobjects(value);
    }
    if let Some(value) = options.svg_form_xobjects {
        builder = builder.svg_form_xobjects(value);
    }
    if let Some(value) = options.svg_raster_fallback {
        builder = builder.svg_raster_fallback(value);
    }
    if let Some(value) = options.unicode_support {
        builder = builder.unicode_support(value);
    }
    if let Some(value) = options.shape_text {
        builder = builder.shape_text(value);
    }
    if let Some(value) = options.unicode_metrics {
        builder = builder.unicode_metrics(value);
    }
    if let Some(value) = options.pdf_version.as_deref() {
        builder = builder.pdf_version(parse_pdf_version(value)?);
    }
    if let Some(value) = options.pdf_profile.as_deref() {
        builder = builder.pdf_profile(parse_pdf_profile(value)?);
    }
    if let Some(value) = options.color_space.as_deref() {
        builder = builder.color_space(match normalized(value).as_str() {
            "rgb" => ColorSpace::Rgb,
            "cmyk" => ColorSpace::Cmyk,
            _ => {
                return Err(call_error(
                    FbStatusCode::InvalidOptions,
                    format!("unsupported color space {value:?}"),
                ));
            }
        });
    }
    if let Some(intent) = options.output_intent {
        let icc_profile = match (
            intent.icc_profile_path.as_deref(),
            intent.icc_profile_base64.as_deref(),
        ) {
            (Some(_), Some(_)) => {
                return Err(call_error(
                    FbStatusCode::InvalidOptions,
                    "outputIntent cannot set both iccProfilePath and iccProfileBase64",
                ));
            }
            (Some(path), None) => std::fs::read(path).map_err(|error| {
                call_error(
                    FbStatusCode::IoFailed,
                    format!("could not read output-intent ICC profile {path:?}: {error}"),
                )
            })?,
            (None, Some(encoded)) => base64::engine::general_purpose::STANDARD
                .decode(encoded)
                .map_err(|error| {
                    call_error(
                        FbStatusCode::InvalidOptions,
                        format!("output-intent ICC base64 is invalid: {error}"),
                    )
                })?,
            (None, None) => {
                return Err(call_error(
                    FbStatusCode::InvalidOptions,
                    "outputIntent requires iccProfilePath or iccProfileBase64",
                ));
            }
        };
        builder = builder.output_intent(OutputIntent::new(
            icc_profile,
            intent.components,
            intent.identifier,
            intent.info,
        ));
    }
    if let Some(value) = options.document_language {
        builder = builder.document_lang(value);
    }
    if let Some(value) = options.document_title {
        builder = builder.document_title(value);
    }
    if let Some(value) = options.jit_mode.as_deref() {
        builder = builder.jit_mode(parse_jit_mode(value)?);
    }
    if let Some(value) = options.layout_strategy.as_deref() {
        builder = match normalized(value).as_str() {
            "eager" => builder.layout_strategy(LayoutStrategy::Eager),
            "lazy" => builder.layout_strategy(LayoutStrategy::Lazy),
            _ => {
                return Err(call_error(
                    FbStatusCode::InvalidOptions,
                    format!("unsupported layout strategy {value:?}"),
                ));
            }
        };
    }
    builder = builder.accept_lazy_layout_cost(options.accept_lazy_layout_cost);
    if options.lazy_max_passes.is_some() || options.lazy_budget_ms.is_some() {
        builder = builder.lazy_layout_limits(
            options.lazy_max_passes.unwrap_or(4),
            options.lazy_budget_ms.unwrap_or(50.0),
        );
    }
    if let Some(path) = options.debug_log_path {
        builder = builder.debug_log(path);
    }
    if let Some(path) = options.perf_log_path {
        builder = builder.perf_log(path);
    } else if let Some(enabled) = options.perf_enabled {
        builder = builder.perf_enabled(enabled);
    }
    if let Some(header) = options.header {
        builder = builder.page_header(
            header.first,
            header.each,
            header.last,
            header.x_pt,
            header.y_pt,
            header.font_name,
            header.font_size_pt,
            header.color.to_native()?,
        );
    }
    if let Some(header) = options.header_html {
        builder = builder.page_header_html(
            header.first,
            header.each,
            header.last,
            header.x_pt,
            header.y_pt,
            header.width_pt,
            header.height_pt,
        );
    }
    if let Some(footer) = options.footer {
        builder = builder.page_footer(
            footer.first,
            footer.each,
            footer.last,
            footer.x_pt,
            footer.y_pt,
            footer.font_name,
            footer.font_size_pt,
            footer.color.to_native()?,
        );
    }
    if let Some(watermark) = options.watermark {
        if !watermark.opacity.is_finite() || !(0.0..=1.0).contains(&watermark.opacity) {
            return Err(call_error(
                FbStatusCode::InvalidOptions,
                "watermark.opacity must be finite and in the range 0..=1",
            ));
        }
        let kind = match normalized(&watermark.kind).as_str() {
            "text" => WatermarkKind::Text(watermark.value),
            "html" => WatermarkKind::Html(watermark.value),
            "image" => WatermarkKind::Image(watermark.value),
            _ => {
                return Err(call_error(
                    FbStatusCode::InvalidOptions,
                    "watermark.kind must be text, html, or image",
                ));
            }
        };
        let layer = match normalized(&watermark.layer).as_str() {
            "background" | "underlay" => WatermarkLayer::Background,
            "overlay" => WatermarkLayer::Overlay,
            _ => {
                return Err(call_error(
                    FbStatusCode::InvalidOptions,
                    "watermark.layer must be background or overlay",
                ));
            }
        };
        let semantics = match normalized(&watermark.semantics).as_str() {
            "visual" => WatermarkSemantics::Visual,
            "artifact" => WatermarkSemantics::Artifact,
            "ocg" => WatermarkSemantics::Ocg,
            _ => {
                return Err(call_error(
                    FbStatusCode::InvalidOptions,
                    "watermark.semantics must be visual, artifact, or ocg",
                ));
            }
        };
        builder = builder.watermark(WatermarkSpec {
            kind,
            layer,
            semantics,
            opacity: watermark.opacity,
            rotation_deg: watermark.rotation_deg,
            font_name: watermark.font_name,
            font_size: Pt::from_f32(watermark.font_size_pt),
            color: watermark.color.to_native()?,
        });
    }
    if !options.paginated_context.is_empty() {
        let mut operations = HashMap::new();
        for (name, operation) in options.paginated_context {
            let parsed = PaginatedContextSpec::parse_op(&operation).ok_or_else(|| {
                call_error(
                    FbStatusCode::InvalidOptions,
                    format!("unsupported paginated context operation {operation:?}"),
                )
            })?;
            operations.insert(name, parsed);
        }
        builder = builder.paginated_context(PaginatedContextSpec::new(operations));
    }
    if let Some(binding) = options.template_binding {
        builder = builder.template_binding_spec(TemplateBindingSpec {
            default_template_id: binding.default_template_id,
            by_page_template: binding.by_page_template,
            by_feature: binding.by_feature,
            feature_prefix: binding.feature_prefix,
        });
    }
    if !options.assets.is_empty() {
        let mut bundle = AssetBundle::default();
        for asset in options.assets {
            bundle.add(asset_from_options(asset)?);
        }
        builder = builder.register_bundle(bundle);
    }

    builder.build().map_err(map_engine_error)
}

fn page_data_value(value: &PageDataValue) -> Value {
    match value {
        PageDataValue::Every(values) => json!({ "kind": "every", "values": values }),
        PageDataValue::Count(value) => json!({ "kind": "count", "value": value }),
        PageDataValue::Sum { scale, value } => {
            json!({ "kind": "sum", "scale": scale, "value": value })
        }
    }
}

fn page_data_json(context: &PageDataContext) -> Value {
    let pages = context
        .pages
        .iter()
        .map(|page| {
            let mut values = serde_json::Map::new();
            let mut keys = page.keys().collect::<Vec<_>>();
            keys.sort();
            for key in keys {
                values.insert(key.clone(), page_data_value(&page[key]));
            }
            Value::Object(values)
        })
        .collect::<Vec<_>>();
    let mut totals = serde_json::Map::new();
    let mut keys = context.totals.keys().collect::<Vec<_>>();
    keys.sort();
    for key in keys {
        totals.insert(key.clone(), page_data_value(&context.totals[key]));
    }
    json!({
        "pageCount": context.page_count,
        "pages": pages,
        "totals": totals,
    })
}

fn inspect_json(path: &str) -> FbCallResult<Value> {
    let report = inspect_pdf_path(Path::new(path)).map_err(map_inspect_error)?;
    let compatibility_issues = composition_compatibility_issues(&report)
        .into_iter()
        .map(|issue| issue.as_str())
        .collect::<Vec<_>>();
    Ok(json!({
        "schema": "fullbleed.dotnet.inspect.v1",
        "path": path,
        "pdfVersion": report.pdf_version,
        "pageCount": report.page_count,
        "encrypted": report.encrypted,
        "fileSizeBytes": report.file_size_bytes,
        "warnings": report.warnings.iter().map(|warning| json!({
            "code": warning.code,
            "message": warning.message,
        })).collect::<Vec<_>>(),
        "profile": {
            "claims": report.profile.claims,
            "metadataPresent": report.profile.metadata_present,
            "outputIntentPresent": report.profile.output_intent_present,
            "structTreeRootPresent": report.profile.struct_tree_root_present,
            "markInfoPresent": report.profile.mark_info_present,
            "langPresent": report.profile.lang_present,
            "embeddedFontCount": report.profile.embedded_font_count,
            "embeddedFilesPresent": report.profile.embedded_files_present,
            "pdfDeclarationPresent": report.profile.pdf_declaration_present,
            "dpartRootPresent": report.profile.dpart_root_present,
            "dpartPresent": report.profile.dpart_present,
            "pageDpartPresent": report.profile.page_dpart_present,
            "pdfvtDpartRootNodeValid": report.profile.pdfvt_dpart_root_node_valid,
            "pdfvtDpartParentValid": report.profile.pdfvt_dpart_parent_valid,
            "pdfvtDpartNodeNameListValid": report.profile.pdfvt_dpart_node_name_list_valid,
            "pdfvtDpartLeafValid": report.profile.pdfvt_dpart_leaf_valid,
            "pdfvtDpartPageRangeValid": report.profile.pdfvt_dpart_page_range_valid,
            "pdfvtDpartGraphValid": report.profile.pdfvt_dpart_graph_valid,
            "pdfvtModDateMatchesXmp": report.profile.pdfvt_mod_date_matches_xmp,
            "seedBlockers": report.profile.seed_blockers,
        },
        "composition": {
            "supported": compatibility_issues.is_empty(),
            "issues": compatibility_issues,
        }
    }))
}

#[unsafe(no_mangle)]
pub extern "C" fn fullbleed_dotnet_abi_version() -> u32 {
    ABI_VERSION
}

/// # Safety
/// `out_json` and `out_error_message` must be valid writable out pointers.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fullbleed_dotnet_build_features(
    out_json: *mut FbByteBuffer,
    out_error_message: *mut *mut c_char,
) -> FbStatusCode {
    if unsafe { initialize_error(out_error_message) }.is_err()
        || unsafe { initialize_buffer(out_json, "out_json") }.is_err()
    {
        return FbStatusCode::NullArgument;
    }
    finish_call(out_error_message, || {
        let value = json!({
            "schema": "fullbleed.dotnet.native_features.v1",
            "abiVersion": ABI_VERSION,
            "bindingVersion": env!("CARGO_PKG_VERSION"),
            "svgRaster": cfg!(feature = "svg_raster"),
            "compiledDocument": true,
            "compiledFixedBindings": true,
            "compiledReflowBindings": true,
            "compiledFlowCompressionModes": ["throughput", "compact"],
        });
        // SAFETY: initialized and validated above.
        unsafe { write_json(out_json, &value) }
    })
}

/// # Safety
/// The JSON pointer/length pair must be readable for the duration of the call. Both out pointers
/// must be valid and writable. The returned handle must be released with `fullbleed_engine_free`.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fullbleed_engine_create(
    options_json: *const c_uchar,
    options_len: usize,
    out_engine: *mut *mut c_void,
    out_error_message: *mut *mut c_char,
) -> FbStatusCode {
    if unsafe { initialize_error(out_error_message) }.is_err() || out_engine.is_null() {
        return FbStatusCode::NullArgument;
    }
    // SAFETY: out_engine was validated above.
    unsafe { *out_engine = ptr::null_mut() };
    finish_call(out_error_message, || {
        // SAFETY: parse_json validates the supplied pointer/length pair.
        let options = unsafe { parse_json(options_json, options_len, "engine options") }?;
        let handle = Box::new(EngineHandle {
            engine: build_engine(options)?,
        });
        // SAFETY: out_engine was validated above.
        unsafe { *out_engine = Box::into_raw(handle).cast() };
        Ok(())
    })
}

/// # Safety
/// `handle` must be null or a live pointer returned by `fullbleed_engine_create`, and it must be
/// released at most once.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fullbleed_engine_free(handle: *mut c_void) {
    if !handle.is_null() {
        // SAFETY: pointer must have been returned by fullbleed_engine_create exactly once.
        unsafe { drop(Box::from_raw(handle.cast::<EngineHandle>())) };
    }
}

/// # Safety
/// `handle` must be null or a live pointer returned by `fullbleed_engine_compile`, and it must be
/// released at most once.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fullbleed_compiled_free(handle: *mut c_void) {
    if !handle.is_null() {
        // SAFETY: pointer must have been returned by fullbleed_engine_compile exactly once.
        unsafe { drop(Box::from_raw(handle.cast::<CompiledHandle>())) };
    }
}

/// # Safety
/// `handle` must be live, input pointer/length pairs must be readable, and output pointers must be
/// valid and writable. Returned buffers/errors must be freed with the matching bridge functions.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fullbleed_engine_render_pdf(
    handle: *mut c_void,
    html_ptr: *const c_uchar,
    html_len: usize,
    css_ptr: *const c_uchar,
    css_len: usize,
    out_pdf: *mut FbByteBuffer,
    out_error_message: *mut *mut c_char,
) -> FbStatusCode {
    if unsafe { initialize_error(out_error_message) }.is_err()
        || unsafe { initialize_buffer(out_pdf, "out_pdf") }.is_err()
    {
        return FbStatusCode::NullArgument;
    }
    finish_call(out_error_message, || {
        // SAFETY: helpers validate handles and input pointer/length pairs.
        let engine = unsafe { engine_from_handle(handle) }?;
        let html = unsafe { read_utf8(html_ptr, html_len, "html") }?;
        let css = unsafe { read_utf8(css_ptr, css_len, "css") }?;
        let pdf = engine
            .render_to_buffer(html, css)
            .map_err(map_engine_error)?;
        // SAFETY: initialized and validated above.
        unsafe { write_buffer(out_pdf, pdf) };
        Ok(())
    })
}

/// # Safety
/// `handle` must be live, input pointer/length pairs must be readable, and output pointers must be
/// valid and writable. Returned errors must be freed with `fullbleed_string_free`.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fullbleed_engine_render_pdf_to_file(
    handle: *mut c_void,
    html_ptr: *const c_uchar,
    html_len: usize,
    css_ptr: *const c_uchar,
    css_len: usize,
    path_ptr: *const c_uchar,
    path_len: usize,
    out_bytes_written: *mut usize,
    out_error_message: *mut *mut c_char,
) -> FbStatusCode {
    if unsafe { initialize_error(out_error_message) }.is_err() || out_bytes_written.is_null() {
        return FbStatusCode::NullArgument;
    }
    // SAFETY: validated above.
    unsafe { *out_bytes_written = 0 };
    finish_call(out_error_message, || {
        // SAFETY: helpers validate handles and input pointer/length pairs.
        let engine = unsafe { engine_from_handle(handle) }?;
        let html = unsafe { read_utf8(html_ptr, html_len, "html") }?;
        let css = unsafe { read_utf8(css_ptr, css_len, "css") }?;
        let path = unsafe { read_utf8(path_ptr, path_len, "path") }?;
        let written = engine
            .render_to_file(html, css, path)
            .map_err(map_engine_error)?;
        // SAFETY: validated above.
        unsafe { *out_bytes_written = written };
        Ok(())
    })
}

/// # Safety
/// `handle` must be live, input pointer/length pairs must be readable, and output pointers must be
/// valid and writable. Returned buffers/errors must be freed with the matching bridge functions.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fullbleed_engine_render_with_diagnostics(
    handle: *mut c_void,
    html_ptr: *const c_uchar,
    html_len: usize,
    css_ptr: *const c_uchar,
    css_len: usize,
    out_pdf: *mut FbByteBuffer,
    out_json: *mut FbByteBuffer,
    out_error_message: *mut *mut c_char,
) -> FbStatusCode {
    if unsafe { initialize_error(out_error_message) }.is_err()
        || unsafe { initialize_buffer(out_pdf, "out_pdf") }.is_err()
        || unsafe { initialize_buffer(out_json, "out_json") }.is_err()
    {
        return FbStatusCode::NullArgument;
    }
    finish_call(out_error_message, || {
        // SAFETY: helpers validate handles and input pointer/length pairs.
        let engine = unsafe { engine_from_handle(handle) }?;
        let html = unsafe { read_utf8(html_ptr, html_len, "html") }?;
        let css = unsafe { read_utf8(css_ptr, css_len, "css") }?;
        let (pdf, page_data, glyphs) = engine
            .render_with_page_data_and_glyph_report(html, css)
            .map_err(map_engine_error)?;
        let diagnostics = json!({
            "schema": "fullbleed.dotnet.render_diagnostics.v1",
            "pageData": page_data.as_ref().map(page_data_json),
            "missingGlyphs": glyphs.missing().into_iter().map(|glyph| json!({
                "codepoint": glyph.codepoint,
                "character": glyph.ch.to_string(),
                "fontsTried": glyph.fonts_tried,
                "count": glyph.count,
            })).collect::<Vec<_>>(),
        });
        // SAFETY: initialized and validated above.
        unsafe {
            write_buffer(out_pdf, pdf);
            write_json(out_json, &diagnostics)?;
        }
        Ok(())
    })
}

/// # Safety
/// `handle` must be live, input pointer/length pairs must be readable, and output pointers must be
/// valid and writable. Returned buffers/errors must be freed with the matching bridge functions.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fullbleed_engine_render_with_metrics(
    handle: *mut c_void,
    html_ptr: *const c_uchar,
    html_len: usize,
    css_ptr: *const c_uchar,
    css_len: usize,
    out_pdf: *mut FbByteBuffer,
    out_json: *mut FbByteBuffer,
    out_error_message: *mut *mut c_char,
) -> FbStatusCode {
    if unsafe { initialize_error(out_error_message) }.is_err()
        || unsafe { initialize_buffer(out_pdf, "out_pdf") }.is_err()
        || unsafe { initialize_buffer(out_json, "out_json") }.is_err()
    {
        return FbStatusCode::NullArgument;
    }
    finish_call(out_error_message, || {
        // SAFETY: helpers validate handles and input pointer/length pairs.
        let engine = unsafe { engine_from_handle(handle) }?;
        let html = unsafe { read_utf8(html_ptr, html_len, "html") }?;
        let css = unsafe { read_utf8(css_ptr, css_len, "css") }?;
        let (pdf, metrics) = engine
            .render_with_metrics(html, css)
            .map_err(map_engine_error)?;
        let value = json!({
            "schema": "fullbleed.dotnet.render_metrics.v1",
            "totalRenderMs": metrics.total_render_ms,
            "totalBytes": metrics.total_bytes,
            "pages": metrics.pages.iter().map(|page| json!({
                "pageNumber": page.page_number,
                "renderMs": page.render_ms,
                "commandCount": page.command_count,
                "flowableCount": page.flowable_count,
                "contentBytes": page.content_bytes,
            })).collect::<Vec<_>>(),
        });
        // SAFETY: initialized and validated above.
        unsafe {
            write_buffer(out_pdf, pdf);
            write_json(out_json, &value)?;
        }
        Ok(())
    })
}

/// # Safety
/// `handle` must be live, all input pointer/length pairs must be readable, and output pointers
/// must be valid and writable. Returned buffers/errors require the matching free functions.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fullbleed_engine_render_image_pages_to_dir(
    handle: *mut c_void,
    html_ptr: *const c_uchar,
    html_len: usize,
    css_ptr: *const c_uchar,
    css_len: usize,
    out_dir_ptr: *const c_uchar,
    out_dir_len: usize,
    stem_ptr: *const c_uchar,
    stem_len: usize,
    dpi: u32,
    out_json: *mut FbByteBuffer,
    out_error_message: *mut *mut c_char,
) -> FbStatusCode {
    if unsafe { initialize_error(out_error_message) }.is_err()
        || unsafe { initialize_buffer(out_json, "out_json") }.is_err()
    {
        return FbStatusCode::NullArgument;
    }
    finish_call(out_error_message, || {
        // SAFETY: helpers validate handles and input pointer/length pairs.
        let engine = unsafe { engine_from_handle(handle) }?;
        let html = unsafe { read_utf8(html_ptr, html_len, "html") }?;
        let css = unsafe { read_utf8(css_ptr, css_len, "css") }?;
        let out_dir = unsafe { read_utf8(out_dir_ptr, out_dir_len, "out_dir") }?;
        let stem = unsafe { read_utf8(stem_ptr, stem_len, "stem") }?;
        let paths = engine
            .render_image_pages_to_dir(html, css, out_dir, stem, dpi)
            .map_err(map_engine_error)?;
        let value = json!({
            "schema": "fullbleed.dotnet.image_pages.v1",
            "paths": paths.iter().map(|path| path.to_string_lossy()).collect::<Vec<_>>(),
            "dpi": dpi,
        });
        // SAFETY: initialized and validated above.
        unsafe { write_json(out_json, &value) }
    })
}

/// # Safety
/// `handle` must be live, all input pointer/length pairs must be readable, and output pointers
/// must be valid and writable. Returned buffers/errors require the matching free functions.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fullbleed_engine_render_finalized_pdf_image_pages_to_dir(
    handle: *mut c_void,
    pdf_path_ptr: *const c_uchar,
    pdf_path_len: usize,
    out_dir_ptr: *const c_uchar,
    out_dir_len: usize,
    stem_ptr: *const c_uchar,
    stem_len: usize,
    dpi: u32,
    out_json: *mut FbByteBuffer,
    out_error_message: *mut *mut c_char,
) -> FbStatusCode {
    if unsafe { initialize_error(out_error_message) }.is_err()
        || unsafe { initialize_buffer(out_json, "out_json") }.is_err()
    {
        return FbStatusCode::NullArgument;
    }
    finish_call(out_error_message, || {
        // SAFETY: helpers validate handles and input pointer/length pairs.
        let engine = unsafe { engine_from_handle(handle) }?;
        let pdf_path = unsafe { read_utf8(pdf_path_ptr, pdf_path_len, "pdf_path") }?;
        let out_dir = unsafe { read_utf8(out_dir_ptr, out_dir_len, "out_dir") }?;
        let stem = unsafe { read_utf8(stem_ptr, stem_len, "stem") }?;
        let paths = engine
            .render_finalized_pdf_image_pages_to_dir(pdf_path, out_dir, stem, dpi)
            .map_err(map_engine_error)?;
        let value = json!({
            "schema": "fullbleed.dotnet.image_pages.v1",
            "paths": paths.iter().map(|path| path.to_string_lossy()).collect::<Vec<_>>(),
            "dpi": dpi,
        });
        // SAFETY: initialized and validated above.
        unsafe { write_json(out_json, &value) }
    })
}

fn render_batch(engine: &FullBleed, request: &BatchRequest) -> FbCallResult<(Vec<u8>, Value)> {
    if request.jobs.is_empty() {
        return Err(map_engine_error(FullBleedError::EmptyDocumentSet));
    }
    let same_css = request
        .jobs
        .windows(2)
        .all(|pair| pair[0].css == pair[1].css);
    let parallel_used = request.parallel && same_css;
    let (pdf, page_data) = if same_css {
        let html = request
            .jobs
            .iter()
            .map(|job| job.html.clone())
            .collect::<Vec<_>>();
        let css = &request.jobs[0].css;
        if parallel_used && request.include_page_data {
            let (pdf, contexts) = engine
                .render_many_to_buffer_parallel_with_page_data(&html, css)
                .map_err(map_engine_error)?;
            let values = contexts
                .iter()
                .map(|value| value.as_ref().map(page_data_json))
                .collect::<Vec<_>>();
            (pdf, Some(values))
        } else {
            let pdf = if parallel_used {
                engine
                    .render_many_to_buffer_parallel(&html, css)
                    .map_err(map_engine_error)?
            } else {
                engine
                    .render_many_to_buffer(&html, css)
                    .map_err(map_engine_error)?
            };
            (pdf, None)
        }
    } else {
        let jobs = request
            .jobs
            .iter()
            .map(|job| (job.html.clone(), job.css.clone()))
            .collect::<Vec<_>>();
        (
            engine
                .render_many_to_buffer_with_css(&jobs)
                .map_err(map_engine_error)?,
            None,
        )
    };
    let diagnostics = json!({
        "schema": "fullbleed.dotnet.batch_result.v1",
        "jobCount": request.jobs.len(),
        "parallelRequested": request.parallel,
        "parallelUsed": parallel_used,
        "sharedCss": same_css,
        "pageData": page_data,
    });
    Ok((pdf, diagnostics))
}

fn render_batch_to_file(
    engine: &FullBleed,
    request: &BatchRequest,
    path: &str,
) -> FbCallResult<(usize, Value)> {
    if request.jobs.is_empty() {
        return Err(map_engine_error(FullBleedError::EmptyDocumentSet));
    }
    let same_css = request
        .jobs
        .windows(2)
        .all(|pair| pair[0].css == pair[1].css);
    let parallel_used = request.parallel && same_css;
    let (bytes_written, page_data) = if same_css {
        let html = request
            .jobs
            .iter()
            .map(|job| job.html.clone())
            .collect::<Vec<_>>();
        let css = &request.jobs[0].css;
        if parallel_used && request.include_page_data {
            let (written, contexts) = engine
                .render_many_to_file_parallel_with_page_data(&html, css, path)
                .map_err(map_engine_error)?;
            let values = contexts
                .iter()
                .map(|value| value.as_ref().map(page_data_json))
                .collect::<Vec<_>>();
            (written, Some(values))
        } else {
            let written = if parallel_used {
                engine.render_many_to_file_parallel(&html, css, path)
            } else {
                engine.render_many_to_file(&html, css, path)
            }
            .map_err(map_engine_error)?;
            (written, None)
        }
    } else {
        let jobs = request
            .jobs
            .iter()
            .map(|job| (job.html.clone(), job.css.clone()))
            .collect::<Vec<_>>();
        (
            engine
                .render_many_to_file_with_css(&jobs, path)
                .map_err(map_engine_error)?,
            None,
        )
    };
    let diagnostics = json!({
        "schema": "fullbleed.dotnet.batch_result.v1",
        "jobCount": request.jobs.len(),
        "parallelRequested": request.parallel,
        "parallelUsed": parallel_used,
        "sharedCss": same_css,
        "pageData": page_data,
    });
    Ok((bytes_written, diagnostics))
}

/// # Safety
/// `handle` must be live, the request pointer/length pair must be readable, and output pointers
/// must be valid and writable. Returned buffers/errors require the matching free functions.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fullbleed_engine_render_batch(
    handle: *mut c_void,
    request_json: *const c_uchar,
    request_len: usize,
    out_pdf: *mut FbByteBuffer,
    out_json: *mut FbByteBuffer,
    out_error_message: *mut *mut c_char,
) -> FbStatusCode {
    if unsafe { initialize_error(out_error_message) }.is_err()
        || unsafe { initialize_buffer(out_pdf, "out_pdf") }.is_err()
        || unsafe { initialize_buffer(out_json, "out_json") }.is_err()
    {
        return FbStatusCode::NullArgument;
    }
    finish_call(out_error_message, || {
        // SAFETY: helpers validate handles and input pointer/length pairs.
        let engine = unsafe { engine_from_handle(handle) }?;
        let request = unsafe { parse_json(request_json, request_len, "batch request") }?;
        let (pdf, diagnostics) = render_batch(engine, &request)?;
        // SAFETY: initialized and validated above.
        unsafe {
            write_buffer(out_pdf, pdf);
            write_json(out_json, &diagnostics)?;
        }
        Ok(())
    })
}

/// # Safety
/// `handle` must be live, all input pointer/length pairs must be readable, and output pointers
/// must be valid and writable. Returned buffers/errors require the matching free functions.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fullbleed_engine_render_batch_to_file(
    handle: *mut c_void,
    request_json: *const c_uchar,
    request_len: usize,
    path_ptr: *const c_uchar,
    path_len: usize,
    out_bytes_written: *mut usize,
    out_json: *mut FbByteBuffer,
    out_error_message: *mut *mut c_char,
) -> FbStatusCode {
    if unsafe { initialize_error(out_error_message) }.is_err()
        || out_bytes_written.is_null()
        || unsafe { initialize_buffer(out_json, "out_json") }.is_err()
    {
        return FbStatusCode::NullArgument;
    }
    // SAFETY: validated above.
    unsafe { *out_bytes_written = 0 };
    finish_call(out_error_message, || {
        // SAFETY: helpers validate handles and input pointer/length pairs.
        let engine = unsafe { engine_from_handle(handle) }?;
        let request = unsafe { parse_json(request_json, request_len, "batch request") }?;
        let path = unsafe { read_utf8(path_ptr, path_len, "path") }?;
        let (written, diagnostics) = render_batch_to_file(engine, &request, path)?;
        // SAFETY: initialized and validated above.
        unsafe {
            *out_bytes_written = written;
            write_json(out_json, &diagnostics)?;
        }
        Ok(())
    })
}

/// # Safety
/// `handle` must be live, input pointer/length pairs must be readable, and output pointers must be
/// valid and writable. The returned handle must be released with `fullbleed_compiled_free`.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fullbleed_engine_compile(
    handle: *mut c_void,
    html_ptr: *const c_uchar,
    html_len: usize,
    css_ptr: *const c_uchar,
    css_len: usize,
    out_compiled: *mut *mut c_void,
    out_error_message: *mut *mut c_char,
) -> FbStatusCode {
    if unsafe { initialize_error(out_error_message) }.is_err() || out_compiled.is_null() {
        return FbStatusCode::NullArgument;
    }
    // SAFETY: validated above.
    unsafe { *out_compiled = ptr::null_mut() };
    finish_call(out_error_message, || {
        // SAFETY: helpers validate handles and input pointer/length pairs.
        let engine = unsafe { engine_from_handle(handle) }?;
        let html = unsafe { read_utf8(html_ptr, html_len, "html") }?;
        let css = unsafe { read_utf8(css_ptr, css_len, "css") }?;
        let document = engine
            .compile_document(html, css)
            .map_err(map_engine_error)?;
        let compiled = Box::new(CompiledHandle { document });
        // SAFETY: validated above.
        unsafe { *out_compiled = Box::into_raw(compiled).cast() };
        Ok(())
    })
}

/// # Safety
/// `handle` must be a live compiled-document handle and output pointers must be valid and writable.
/// Returned buffers/errors require the matching free functions.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fullbleed_compiled_stats(
    handle: *mut c_void,
    out_json: *mut FbByteBuffer,
    out_error_message: *mut *mut c_char,
) -> FbStatusCode {
    if unsafe { initialize_error(out_error_message) }.is_err()
        || unsafe { initialize_buffer(out_json, "out_json") }.is_err()
    {
        return FbStatusCode::NullArgument;
    }
    finish_call(out_error_message, || {
        // SAFETY: helper validates the handle.
        let compiled = unsafe { compiled_from_handle(handle) }?;
        let value = json!({
            "schema": "fullbleed.dotnet.compiled_stats.v1",
            "pageCount": compiled.page_count(),
            "commandCount": compiled.command_count(),
            "compileMs": compiled.compile_time_ms(),
            "bindingSlots": compiled.binding_slots(),
            "bindingProgramPageCount": compiled.binding_program_page_count(),
            "bindingProgramCommandCount": compiled.binding_program_command_count(),
            "reflowProgramReady": compiled.reflow_program_ready(),
            "reflowProgramError": compiled.reflow_program_error(),
            "reflowBindingSlots": compiled.reflow_binding_slots(),
            "reflowProgramNodeCount": compiled.reflow_program_node_count(),
            "reflowProgramBindingTextNodeCount": compiled.reflow_program_binding_text_node_count(),
            "reflowProgramHtmlBindingNodeCount": compiled.reflow_program_html_binding_node_count(),
            "reflowCompressionModes": ["throughput", "compact"],
            "reflowDefaultCompression": "throughput",
        });
        // SAFETY: initialized and validated above.
        unsafe { write_json(out_json, &value) }
    })
}

/// # Safety
/// `handle` must be a live compiled-document handle and output pointers must be valid and writable.
/// Returned buffers/errors require the matching free functions.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fullbleed_compiled_render(
    handle: *mut c_void,
    copies: usize,
    out_pdf: *mut FbByteBuffer,
    out_error_message: *mut *mut c_char,
) -> FbStatusCode {
    if unsafe { initialize_error(out_error_message) }.is_err()
        || unsafe { initialize_buffer(out_pdf, "out_pdf") }.is_err()
    {
        return FbStatusCode::NullArgument;
    }
    finish_call(out_error_message, || {
        // SAFETY: helper validates the handle.
        let compiled = unsafe { compiled_from_handle(handle) }?;
        let pdf = if copies == 1 {
            compiled.render_to_buffer()
        } else {
            compiled.render_many_to_buffer(copies)
        }
        .map_err(map_engine_error)?;
        // SAFETY: initialized and validated above.
        unsafe { write_buffer(out_pdf, pdf) };
        Ok(())
    })
}

/// # Safety
/// `handle` must be live, the path pointer/length pair must be readable, and output pointers must
/// be valid and writable. Returned errors require `fullbleed_string_free`.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fullbleed_compiled_render_to_file(
    handle: *mut c_void,
    copies: usize,
    path_ptr: *const c_uchar,
    path_len: usize,
    out_bytes_written: *mut usize,
    out_error_message: *mut *mut c_char,
) -> FbStatusCode {
    if unsafe { initialize_error(out_error_message) }.is_err() || out_bytes_written.is_null() {
        return FbStatusCode::NullArgument;
    }
    // SAFETY: validated above.
    unsafe { *out_bytes_written = 0 };
    finish_call(out_error_message, || {
        // SAFETY: helpers validate handle and input pointer/length pair.
        let compiled = unsafe { compiled_from_handle(handle) }?;
        let path = unsafe { read_utf8(path_ptr, path_len, "path") }?;
        let written = if copies == 1 {
            compiled.render_to_file(path).map_err(map_engine_error)?
        } else {
            let file = std::fs::File::create(path).map_err(|error| {
                call_error(
                    FbStatusCode::IoFailed,
                    format!("could not create {path:?}: {error}"),
                )
            })?;
            let mut writer = std::io::BufWriter::new(file);
            let written = compiled
                .render_many_to_writer(copies, &mut writer)
                .map_err(map_engine_error)?;
            std::io::Write::flush(&mut writer).map_err(|error| {
                call_error(
                    FbStatusCode::IoFailed,
                    format!("could not flush {path:?}: {error}"),
                )
            })?;
            written
        };
        // SAFETY: validated above.
        unsafe { *out_bytes_written = written };
        Ok(())
    })
}

/// # Safety
/// `handle` must be live, all input pointer/length pairs must be readable, and output pointers
/// must be valid and writable. Returned buffers/errors require the matching free functions.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fullbleed_compiled_render_bindings(
    handle: *mut c_void,
    bindings_json: *const c_uchar,
    bindings_len: usize,
    reflow: bool,
    compression_ptr: *const c_uchar,
    compression_len: usize,
    out_pdf: *mut FbByteBuffer,
    out_error_message: *mut *mut c_char,
) -> FbStatusCode {
    if unsafe { initialize_error(out_error_message) }.is_err()
        || unsafe { initialize_buffer(out_pdf, "out_pdf") }.is_err()
    {
        return FbStatusCode::NullArgument;
    }
    finish_call(out_error_message, || {
        // SAFETY: helpers validate handle and input pointer/length pairs.
        let compiled = unsafe { compiled_from_handle(handle) }?;
        let bindings: HashMap<String, Vec<String>> =
            unsafe { parse_json(bindings_json, bindings_len, "bindings") }?;
        let pdf = if reflow {
            let compression =
                unsafe { read_utf8(compression_ptr, compression_len, "compression") }?;
            compiled.render_reflow_bindings_to_buffer_with_options(
                &bindings,
                CompiledReflowOptions {
                    compression: parse_compression(compression)?,
                },
            )
        } else {
            compiled.render_bindings_to_buffer(&bindings)
        }
        .map_err(map_engine_error)?;
        // SAFETY: initialized and validated above.
        unsafe { write_buffer(out_pdf, pdf) };
        Ok(())
    })
}

/// # Safety
/// `handle` must be live, all input pointer/length pairs must be readable, and output pointers
/// must be valid and writable. Returned errors require `fullbleed_string_free`.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fullbleed_compiled_render_bindings_to_file(
    handle: *mut c_void,
    bindings_json: *const c_uchar,
    bindings_len: usize,
    reflow: bool,
    compression_ptr: *const c_uchar,
    compression_len: usize,
    path_ptr: *const c_uchar,
    path_len: usize,
    out_bytes_written: *mut usize,
    out_error_message: *mut *mut c_char,
) -> FbStatusCode {
    if unsafe { initialize_error(out_error_message) }.is_err() || out_bytes_written.is_null() {
        return FbStatusCode::NullArgument;
    }
    // SAFETY: validated above.
    unsafe { *out_bytes_written = 0 };
    finish_call(out_error_message, || {
        // SAFETY: helpers validate handle and input pointer/length pairs.
        let compiled = unsafe { compiled_from_handle(handle) }?;
        let bindings: HashMap<String, Vec<String>> =
            unsafe { parse_json(bindings_json, bindings_len, "bindings") }?;
        let path = unsafe { read_utf8(path_ptr, path_len, "path") }?;
        let written = if reflow {
            let compression =
                unsafe { read_utf8(compression_ptr, compression_len, "compression") }?;
            compiled.render_reflow_bindings_to_file_with_options(
                &bindings,
                path,
                CompiledReflowOptions {
                    compression: parse_compression(compression)?,
                },
            )
        } else {
            compiled.render_bindings_to_file(&bindings, path)
        }
        .map_err(map_engine_error)?;
        // SAFETY: validated above.
        unsafe { *out_bytes_written = written };
        Ok(())
    })
}

/// # Safety
/// The path pointer/length pair must be readable and output pointers must be valid and writable.
/// Returned buffers/errors require the matching free functions.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fullbleed_inspect_pdf(
    path_ptr: *const c_uchar,
    path_len: usize,
    out_json: *mut FbByteBuffer,
    out_error_message: *mut *mut c_char,
) -> FbStatusCode {
    if unsafe { initialize_error(out_error_message) }.is_err()
        || unsafe { initialize_buffer(out_json, "out_json") }.is_err()
    {
        return FbStatusCode::NullArgument;
    }
    finish_call(out_error_message, || {
        // SAFETY: helper validates input pointer/length pair.
        let path = unsafe { read_utf8(path_ptr, path_len, "path") }?;
        let value = inspect_json(path)?;
        // SAFETY: initialized and validated above.
        unsafe { write_json(out_json, &value) }
    })
}

/// # Safety
/// All input pointer/length pairs must be readable and output pointers must be valid and writable.
/// Returned buffers/errors require the matching free functions.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fullbleed_finalize_stamp(
    template_ptr: *const c_uchar,
    template_len: usize,
    overlay_ptr: *const c_uchar,
    overlay_len: usize,
    output_ptr: *const c_uchar,
    output_len: usize,
    request_json: *const c_uchar,
    request_len: usize,
    out_json: *mut FbByteBuffer,
    out_error_message: *mut *mut c_char,
) -> FbStatusCode {
    if unsafe { initialize_error(out_error_message) }.is_err()
        || unsafe { initialize_buffer(out_json, "out_json") }.is_err()
    {
        return FbStatusCode::NullArgument;
    }
    finish_call(out_error_message, || {
        // SAFETY: helpers validate input pointer/length pairs.
        let template = unsafe { read_utf8(template_ptr, template_len, "template") }?;
        let overlay = unsafe { read_utf8(overlay_ptr, overlay_len, "overlay") }?;
        let output = unsafe { read_utf8(output_ptr, output_len, "output") }?;
        let request: StampRequest =
            unsafe { parse_json(request_json, request_len, "stamp request") }?;
        let page_map = request.page_map.map(|items| {
            items
                .into_iter()
                .map(|item| (item[0], item[1]))
                .collect::<Vec<_>>()
        });
        let summary = stamp_overlay_on_template_pdf(
            Path::new(template),
            Path::new(overlay),
            Path::new(output),
            page_map.as_deref(),
            request.dx,
            request.dy,
        )
        .map_err(map_engine_error)?;
        let value = json!({
            "schema": "fullbleed.dotnet.finalize_stamp.v1",
            "pagesWritten": summary.pages_written,
            "outputPath": output,
        });
        // SAFETY: initialized and validated above.
        unsafe { write_json(out_json, &value) }
    })
}

/// # Safety
/// All input pointer/length pairs must be readable and output pointers must be valid and writable.
/// Returned buffers/errors require the matching free functions.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fullbleed_finalize_compose(
    overlay_ptr: *const c_uchar,
    overlay_len: usize,
    output_ptr: *const c_uchar,
    output_len: usize,
    request_json: *const c_uchar,
    request_len: usize,
    out_json: *mut FbByteBuffer,
    out_error_message: *mut *mut c_char,
) -> FbStatusCode {
    if unsafe { initialize_error(out_error_message) }.is_err()
        || unsafe { initialize_buffer(out_json, "out_json") }.is_err()
    {
        return FbStatusCode::NullArgument;
    }
    finish_call(out_error_message, || {
        // SAFETY: helpers validate input pointer/length pairs.
        let overlay = unsafe { read_utf8(overlay_ptr, overlay_len, "overlay") }?;
        let output = unsafe { read_utf8(output_ptr, output_len, "output") }?;
        let request: ComposeRequest =
            unsafe { parse_json(request_json, request_len, "compose request") }?;
        let annotation_mode = parse_annotation_mode(request.annotation_mode.as_deref())?;
        let mut catalog = TemplateCatalog::default();
        for item in request.templates {
            catalog
                .insert(TemplateAsset {
                    template_id: item.template_id,
                    pdf_path: PathBuf::from(item.pdf_path),
                    sha256: item.sha256,
                    page_count: item.page_count,
                })
                .map_err(map_engine_error)?;
        }
        let plan = request
            .plan
            .into_iter()
            .map(|item| ComposePagePlan {
                template_id: item.template_id,
                template_page_index: item.template_page_index,
                overlay_page_index: item.overlay_page_index,
                dx: item.dx,
                dy: item.dy,
            })
            .collect::<Vec<_>>();
        let summary = compose_overlay_with_template_catalog_with_annotation_mode(
            &catalog,
            Path::new(overlay),
            Path::new(output),
            &plan,
            annotation_mode,
        )
        .map_err(map_engine_error)?;
        let value = json!({
            "schema": "fullbleed.dotnet.finalize_compose.v1",
            "pagesWritten": summary.pages_written,
            "outputPath": output,
        });
        // SAFETY: initialized and validated above.
        unsafe { write_json(out_json, &value) }
    })
}

// Compatibility entrypoint retained for the original 0.1 proof-of-concept API.
///
/// # Safety
/// Input pointer/length pairs and the optional options pointer must be readable. Output pointers
/// must be valid and writable. Returned buffers/errors require the matching free functions.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fullbleed_render_html_to_pdf(
    html_ptr: *const c_uchar,
    html_len: usize,
    css_ptr: *const c_uchar,
    css_len: usize,
    options: *const FbRenderOptions,
    out_pdf: *mut FbByteBuffer,
    out_error_message: *mut *mut c_char,
) -> FbStatusCode {
    if unsafe { initialize_error(out_error_message) }.is_err()
        || unsafe { initialize_buffer(out_pdf, "out_pdf") }.is_err()
    {
        return FbStatusCode::NullArgument;
    }
    finish_call(out_error_message, || {
        // SAFETY: helpers validate input pointer/length pairs.
        let html = unsafe { read_utf8(html_ptr, html_len, "html") }?;
        let css = unsafe { read_utf8(css_ptr, css_len, "css") }?;
        let options = if options.is_null() {
            FbRenderOptions::default()
        } else {
            // SAFETY: a non-null options pointer must refer to an FbRenderOptions value.
            unsafe { *options }
        };
        if !options.page_width_pt.is_finite()
            || options.page_width_pt <= 0.0
            || !options.page_height_pt.is_finite()
            || options.page_height_pt <= 0.0
        {
            return Err(call_error(
                FbStatusCode::InvalidOptions,
                "page dimensions must be finite and > 0",
            ));
        }
        let margins = MarginsOptions {
            top_pt: options.margin_top_pt,
            right_pt: options.margin_right_pt,
            bottom_pt: options.margin_bottom_pt,
            left_pt: options.margin_left_pt,
        }
        .to_native()?;
        let engine = FullBleed::builder()
            .page_size(Size {
                width: Pt::from_f32(options.page_width_pt),
                height: Pt::from_f32(options.page_height_pt),
            })
            .margins(margins)
            .build()
            .map_err(map_engine_error)?;
        let pdf = engine
            .render_to_buffer(html, css)
            .map_err(map_engine_error)?;
        // SAFETY: initialized and validated above.
        unsafe { write_buffer(out_pdf, pdf) };
        Ok(())
    })
}

/// # Safety
/// `pointer` and `length` must be an allocation returned by this bridge and must be freed once.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fullbleed_buffer_free(pointer: *mut c_uchar, length: usize) {
    if pointer.is_null() {
        return;
    }
    let raw_slice = ptr::slice_from_raw_parts_mut(pointer, length);
    // SAFETY: pointer/length must be a buffer returned by this library exactly once.
    unsafe { drop(Box::<[u8]>::from_raw(raw_slice)) };
}

/// # Safety
/// `pointer` must be a string returned by this bridge and must be freed at most once.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fullbleed_string_free(pointer: *mut c_char) {
    if pointer.is_null() {
        return;
    }
    // SAFETY: pointer must be a CString returned by this library exactly once.
    unsafe { drop(CString::from_raw(pointer)) };
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn profile_aliases_are_normalized() {
        assert_eq!(parse_pdf_profile("PDF/UA-2").unwrap(), PdfProfile::PdfUa2);
        assert_eq!(parse_pdf_profile("pdf-a-2b").unwrap(), PdfProfile::PdfA2b);
        assert!(parse_pdf_profile("not-a-profile").is_err());
    }

    #[test]
    fn default_engine_options_build_and_render() {
        let engine = build_engine(EngineOptions::default()).expect("build default engine");
        let pdf = engine
            .render_to_buffer("<p>NATIVE-UNIT-001</p>", "")
            .expect("render PDF");
        assert!(pdf.starts_with(b"%PDF-"));
    }

    #[test]
    fn invalid_margin_is_rejected_before_engine_build() {
        let options = EngineOptions {
            margins: Some(MarginsOptions {
                top_pt: -1.0,
                right_pt: 0.0,
                bottom_pt: 0.0,
                left_pt: 0.0,
            }),
            ..EngineOptions::default()
        };
        let error = build_engine(options).err().expect("invalid margin error");
        assert_eq!(error.0, FbStatusCode::InvalidOptions);
    }
}
