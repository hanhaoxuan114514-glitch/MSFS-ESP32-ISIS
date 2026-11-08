#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "esp_log.h"
#include "esp_err.h"
#include "esp_check.h"
#include "esp_memory_utils.h"
#include "bsp/esp-bsp.h"
#include "bsp/display.h"
#include "lvgl.h"
#include "esp_psram.h"
#include "esp_heap_caps.h"
#include "esp_system.h"
#include "display.h"
#include "ui_isis.h"
#include "flight_data.h"
#include "serial_receiver.h"

static void render_monitor(lv_disp_drv_t *driver, uint32_t elapsed_ms, uint32_t pixels)
{
    (void)driver;
    static unsigned frames;
    ++frames;
    /* 只稀疏打印；记录渲染耗时，不把静态首帧冒充持续 FPS。 */
    if (frames <= 3 || frames % 120 == 0) {
        ESP_LOGI("render", "frame=%u render_ms=%u pixels=%u", frames,
                 (unsigned)elapsed_ms, (unsigned)pixels);
    }
}

void app_main(void)
{
    ESP_LOGI("isis", "A350 ISIS: USB telemetry, LCD-only, no touch input");
    flight_data_init();
    ESP_ERROR_CHECK(serial_receiver_start());
    ESP_LOGI("phase1", "PSRAM size: %u bytes", (unsigned)esp_psram_get_size());
    lv_disp_t *display = NULL;
    if (esp_psram_get_size() != 0) {
        display = display_start_lcd_only();
    }
    if (display == NULL) {
        ESP_LOGE("phase1", "Display validation stopped; inspect hardware logs");
        return;
    }

    bsp_display_lock(0);
    /* 用户确认实物画面需要整体旋转 180°；使用 LVGL 8 的旋转接口。 */
    lv_disp_set_rotation(display, LV_DISP_ROT_180);
    display->driver->monitor_cb = render_monitor;

    if(ui_isis_create()==NULL) {
        bsp_display_unlock();ESP_LOGE("isis", "UI allocation failed");return;
    }
    ESP_LOGI("isis", "Live USB ISIS created; awaiting SimConnect data");

    bsp_display_unlock();

    while (true) {
        flight_data_t sample;flight_data_snapshot(&sample);
        ESP_LOGI("telemetry", "connected=%d seq=%u age_ms=%u IAS=%.1f ALT=%.0f HDG=%.1f P=%.2f B=%.2f",
                 sample.connected,(unsigned)sample.seq,(unsigned)sample.age_ms,sample.ias_kt,
                 sample.altitude_ft,sample.heading_deg,sample.pitch_deg,sample.bank_deg);
        ESP_LOGI("hud", "E1_valid=%d E1=%.0f REV1=%.0f E2_valid=%d E2=%.0f REV2=%.0f fuel_valid=%d FUEL=%.1f VS=%.0f ENGINES=%u E3=%.0f E4=%.0f",
                 sample.connected && sample.engine_valid[0],sample.throttle_pct[0],sample.reverse_pct[0],
                 sample.connected && sample.engine_valid[1],sample.throttle_pct[1],sample.reverse_pct[1],
                 sample.connected && sample.fuel_valid,sample.fuel_pct,sample.vertical_speed_fpm,
                 (unsigned)sample.engine_count,sample.throttle_pct[2],sample.throttle_pct[3]);
        ESP_LOGI("phase1", "heap internal=%u psram=%u minimum=%u",
                 (unsigned)heap_caps_get_free_size(MALLOC_CAP_INTERNAL),
                 (unsigned)heap_caps_get_free_size(MALLOC_CAP_SPIRAM),
                 (unsigned)esp_get_minimum_free_heap_size());
        vTaskDelay(pdMS_TO_TICKS(5000));
    }
}
