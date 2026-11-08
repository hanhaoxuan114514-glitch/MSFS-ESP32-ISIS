/* RGB 端口配置依据 Waveshare BSP 1.1.0 的 bsp_display_lcd_init。
 * 原始组件由 Component Registry 下载，版本和哈希见 dependencies.lock。
 * LCD 初始化命令和 GPIO 全部通过官方 BSP 提供，不在应用中复制或猜测。
 */
#include "display.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "bsp/esp-bsp.h"
#include "bsp/display.h"
#include "esp_check.h"
#include "esp_lvgl_port.h"

static const char *TAG = "lcd_only";

lv_disp_t *display_start_lcd_only(void)
{
    /* 引脚来自官方 V3 IOExpander_general_example；不操作触摸复位口 0。 */
    const uint32_t backlight = IO_EXPANDER_PIN_NUM_1;
    const uint32_t lcd_reset = IO_EXPANDER_PIN_NUM_2;
    esp_io_expander_handle_t io = bsp_io_expander_init();
    if (io == NULL) {
        ESP_LOGE(TAG, "TCA9554 initialization failed");
        return NULL;
    }
    ESP_ERROR_CHECK(esp_io_expander_set_dir(io, backlight | lcd_reset, IO_EXPANDER_OUTPUT));
    ESP_ERROR_CHECK(esp_io_expander_set_level(io, backlight | lcd_reset, 0));
    vTaskDelay(pdMS_TO_TICKS(100));
    ESP_ERROR_CHECK(esp_io_expander_set_level(io, lcd_reset, 1));
    vTaskDelay(pdMS_TO_TICKS(100));

    lvgl_port_cfg_t port_cfg = ESP_LVGL_PORT_INIT_CONFIG();
    ESP_ERROR_CHECK(lvgl_port_init(&port_cfg));
    esp_lcd_panel_handle_t panel = NULL;
    esp_lcd_panel_io_handle_t panel_io = NULL;
    const bsp_display_config_t panel_cfg = {0};
    ESP_ERROR_CHECK(bsp_display_new(&panel_cfg, &panel, &panel_io));

    /* 沿用 BSP 1.1.0 的 RGB 端口配置，跳过它强制执行的触摸启动步骤。 */
    const lvgl_port_display_cfg_t display_cfg = {
        .io_handle = panel_io,
        .panel_handle = panel,
#if CONFIG_BSP_DISPLAY_LVGL_AVOID_TEAR
        .buffer_size = BSP_LCD_H_RES * BSP_LCD_V_RES,
#else
        .buffer_size = BSP_LCD_H_RES * LVGL_BUFFER_HEIGHT,
#endif
        .hres = BSP_LCD_H_RES,
        .vres = BSP_LCD_V_RES,
        .monochrome = false,
        .rotation = {.swap_xy = false, .mirror_x = false, .mirror_y = false},
        .flags = {
            /* 使用官方 ST7701/显示端口的硬件翻转，避免逐像素软件旋转。 */
            .sw_rotate = false,
            .buff_dma = false,
            .buff_spiram = false,
#if CONFIG_BSP_DISPLAY_LVGL_FULL_REFRESH
            .full_refresh = true,
#elif CONFIG_BSP_DISPLAY_LVGL_DIRECT_MODE
            .direct_mode = true,
#endif
        },
    };
    const lvgl_port_display_rgb_cfg_t rgb_cfg = {
        .flags = {
#if CONFIG_BSP_LCD_RGB_BOUNCE_BUFFER_MODE
            .bb_mode = true,
#endif
#if CONFIG_BSP_DISPLAY_LVGL_AVOID_TEAR
            .avoid_tearing = true,
#endif
        },
    };
    lv_disp_t *display = lvgl_port_add_disp_rgb(&display_cfg, &rgb_cfg);
    if (display == NULL) {
        ESP_LOGE(TAG, "LVGL RGB display registration failed");
        return NULL;
    }
    ESP_ERROR_CHECK(esp_io_expander_set_level(io, backlight, 1));
    ESP_LOGI(TAG, "LCD started; backlight enabled; GT911 disabled");
    return display;
}
