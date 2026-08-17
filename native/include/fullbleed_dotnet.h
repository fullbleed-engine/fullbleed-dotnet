#ifndef FULLBLEED_DOTNET_H
#define FULLBLEED_DOTNET_H

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#if defined(_WIN32)
#define FB_API __declspec(dllimport)
#else
#define FB_API
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef enum FbStatusCode {
    FB_STATUS_OK = 0,
    FB_STATUS_NULL_ARGUMENT = 1,
    FB_STATUS_INVALID_UTF8 = 2,
    FB_STATUS_INVALID_OPTIONS = 3,
    FB_STATUS_RENDER_FAILED = 4,
    FB_STATUS_IO_FAILED = 5,
    FB_STATUS_INVALID_HANDLE = 6,
    FB_STATUS_SERIALIZATION_FAILED = 7,
    FB_STATUS_PANIC = 255
} FbStatusCode;

typedef struct FbByteBuffer {
    uint8_t *ptr;
    size_t len;
} FbByteBuffer;

typedef struct FbRenderOptions {
    float page_width_pt;
    float page_height_pt;
    float margin_top_pt;
    float margin_right_pt;
    float margin_bottom_pt;
    float margin_left_pt;
} FbRenderOptions;

FB_API uint32_t fullbleed_dotnet_abi_version(void);
FB_API FbStatusCode fullbleed_dotnet_build_features(FbByteBuffer *out_json, char **out_error);

FB_API FbStatusCode fullbleed_engine_create(
    const uint8_t *options_json, size_t options_len, void **out_engine, char **out_error);
FB_API void fullbleed_engine_free(void *engine);
FB_API FbStatusCode fullbleed_engine_render_pdf(
    void *engine,
    const uint8_t *html, size_t html_len,
    const uint8_t *css, size_t css_len,
    FbByteBuffer *out_pdf, char **out_error);
FB_API FbStatusCode fullbleed_engine_render_pdf_to_file(
    void *engine,
    const uint8_t *html, size_t html_len,
    const uint8_t *css, size_t css_len,
    const uint8_t *path, size_t path_len,
    size_t *out_bytes_written, char **out_error);
FB_API FbStatusCode fullbleed_engine_render_with_diagnostics(
    void *engine,
    const uint8_t *html, size_t html_len,
    const uint8_t *css, size_t css_len,
    FbByteBuffer *out_pdf, FbByteBuffer *out_json, char **out_error);
FB_API FbStatusCode fullbleed_engine_render_with_metrics(
    void *engine,
    const uint8_t *html, size_t html_len,
    const uint8_t *css, size_t css_len,
    FbByteBuffer *out_pdf, FbByteBuffer *out_json, char **out_error);
FB_API FbStatusCode fullbleed_engine_render_image_pages_to_dir(
    void *engine,
    const uint8_t *html, size_t html_len,
    const uint8_t *css, size_t css_len,
    const uint8_t *out_dir, size_t out_dir_len,
    const uint8_t *stem, size_t stem_len,
    uint32_t dpi, FbByteBuffer *out_json, char **out_error);
FB_API FbStatusCode fullbleed_engine_render_finalized_pdf_image_pages_to_dir(
    void *engine,
    const uint8_t *pdf_path, size_t pdf_path_len,
    const uint8_t *out_dir, size_t out_dir_len,
    const uint8_t *stem, size_t stem_len,
    uint32_t dpi, FbByteBuffer *out_json, char **out_error);
FB_API FbStatusCode fullbleed_engine_render_batch(
    void *engine,
    const uint8_t *request_json, size_t request_len,
    FbByteBuffer *out_pdf, FbByteBuffer *out_json, char **out_error);
FB_API FbStatusCode fullbleed_engine_render_batch_to_file(
    void *engine,
    const uint8_t *request_json, size_t request_len,
    const uint8_t *path, size_t path_len,
    size_t *out_bytes_written, FbByteBuffer *out_json, char **out_error);
FB_API FbStatusCode fullbleed_engine_compile(
    void *engine,
    const uint8_t *html, size_t html_len,
    const uint8_t *css, size_t css_len,
    void **out_compiled, char **out_error);

FB_API void fullbleed_compiled_free(void *compiled);
FB_API FbStatusCode fullbleed_compiled_stats(
    void *compiled, FbByteBuffer *out_json, char **out_error);
FB_API FbStatusCode fullbleed_compiled_render(
    void *compiled, size_t copies, FbByteBuffer *out_pdf, char **out_error);
FB_API FbStatusCode fullbleed_compiled_render_to_file(
    void *compiled, size_t copies,
    const uint8_t *path, size_t path_len,
    size_t *out_bytes_written, char **out_error);
FB_API FbStatusCode fullbleed_compiled_render_bindings(
    void *compiled,
    const uint8_t *bindings_json, size_t bindings_len,
    bool reflow,
    const uint8_t *compression, size_t compression_len,
    FbByteBuffer *out_pdf, char **out_error);
FB_API FbStatusCode fullbleed_compiled_render_bindings_to_file(
    void *compiled,
    const uint8_t *bindings_json, size_t bindings_len,
    bool reflow,
    const uint8_t *compression, size_t compression_len,
    const uint8_t *path, size_t path_len,
    size_t *out_bytes_written, char **out_error);

FB_API FbStatusCode fullbleed_inspect_pdf(
    const uint8_t *path, size_t path_len, FbByteBuffer *out_json, char **out_error);
FB_API FbStatusCode fullbleed_finalize_stamp(
    const uint8_t *template_path, size_t template_len,
    const uint8_t *overlay_path, size_t overlay_len,
    const uint8_t *output_path, size_t output_len,
    const uint8_t *request_json, size_t request_len,
    FbByteBuffer *out_json, char **out_error);
FB_API FbStatusCode fullbleed_finalize_compose(
    const uint8_t *overlay_path, size_t overlay_len,
    const uint8_t *output_path, size_t output_len,
    const uint8_t *request_json, size_t request_len,
    FbByteBuffer *out_json, char **out_error);

/* Compatibility entrypoint retained from the initial proof of concept. */
FB_API FbStatusCode fullbleed_render_html_to_pdf(
    const uint8_t *html, size_t html_len,
    const uint8_t *css, size_t css_len,
    const FbRenderOptions *options,
    FbByteBuffer *out_pdf, char **out_error);

/* Release buffers and strings returned by this bridge exactly once. */
FB_API void fullbleed_buffer_free(uint8_t *ptr, size_t len);
FB_API void fullbleed_string_free(char *ptr);

#ifdef __cplusplus
}
#endif

#endif
