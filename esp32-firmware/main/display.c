/* RGB 端口配置依据 Waveshare BSP 1.1.0 的 bsp_display_lcd_init。
 * 原始组件由 Component Registry 下载，版本和哈希见 dependencies.lock。
 * LCD 初始化命令和 GPIO 全部通过官方 BSP 提供，不在应用中复制或猜测。
 */
#include "display.h"
#include "freertos/FreeRTOS.h"
#include "freertos/queue.h"
#include "freertos/task.h"
#include "bsp/esp-bsp.h"
#include "bsp/display.h"
#include "esp_check.h"
#include "esp_lvgl_port.h"
#include "esp_timer.h"
#include <inttypes.h>
#include <stdio.h>
#include <string.h>

static const char *TAG = "lcd_only";

static void (*original_flush)(lv_disp_drv_t *, const lv_area_t *, lv_color_t *);
static void (*original_copy)(lv_draw_ctx_t *, void *, lv_coord_t, const lv_area_t *,
                             void *, lv_coord_t, const lv_area_t *);
static lv_timer_cb_t original_refresh;
static int64_t refresh_start, draw_us, copy_us, flush_us;
static QueueHandle_t profile_queue;

typedef struct {
    unsigned frames, elapsed_ms, pixels, intervals, over_35ms;
    int64_t frame_end_us, total_us, draw_us, copy_us, flush_us, longest_us;
    uint32_t slowest[12];
} profile_sample_t;

static void profile_refresh(lv_timer_t *timer)
{
    refresh_start=esp_timer_get_time();
    draw_us=copy_us=flush_us=0;
    original_refresh(timer);
}

static void profile_flush(lv_disp_drv_t *driver,const lv_area_t *area,lv_color_t *pixels)
{
    int64_t start=esp_timer_get_time();
    original_flush(driver,area,pixels);
    flush_us+=esp_timer_get_time()-start;
}

static void profile_copy(lv_draw_ctx_t *ctx,void *destination,lv_coord_t destination_stride,
                         const lv_area_t *destination_area,void *source,lv_coord_t source_stride,
                         const lv_area_t *source_area)
{
    (void)ctx;
    int64_t start=esp_timer_get_time();
    lv_color_t *dst=(lv_color_t *)destination+destination_area->y1*destination_stride+destination_area->x1;
    const lv_color_t *src=(const lv_color_t *)source+source_area->y1*source_stride+source_area->x1;
    const int width=lv_area_get_width(destination_area),height=lv_area_get_height(destination_area);
    const size_t bytes=width*sizeof(lv_color_t);
    if(width==destination_stride && width==source_stride) {
        memcpy(dst,src,bytes*height);
    } else {
        for(int row=0;row<height;row++) {
            memcpy(dst,src,bytes);
            dst+=destination_stride;
            src+=source_stride;
        }
    }
    copy_us+=esp_timer_get_time()-start;
}

void display_profile_add_draw(int64_t elapsed_us)
{
    draw_us+=elapsed_us;
}

static void profile_log(const profile_sample_t *sample)
{
    char slow_text[96]={0};
    size_t used=0;
    for(unsigned i=0;i<sizeof(sample->slowest)/sizeof(sample->slowest[0]) && sample->slowest[i];i++)
        used+=snprintf(slow_text+used,sizeof(slow_text)-used,"%s%" PRIu32,i?",":"",sample->slowest[i]);
    ESP_LOGI("render","frame=%u render_ms=%u pixels=%u total_us=%" PRId64 " draw_us=%" PRId64
             " copy_us=%" PRId64 " flush_us=%" PRId64 " other_us=%" PRId64
             " intervals=%u over_35ms=%u longest_us=%" PRId64 " slow_us=%s frame_end_us=%" PRId64,
             sample->frames,sample->elapsed_ms,sample->pixels,sample->total_us,sample->draw_us,
             sample->copy_us,sample->flush_us,sample->total_us-sample->draw_us-sample->copy_us-sample->flush_us,
             sample->intervals,sample->over_35ms,sample->longest_us,slow_text,sample->frame_end_us);
}

static void profile_log_task(void *arg)
{
    (void)arg;
    profile_sample_t sample;
    for(;;) {
        if(xQueueReceive(profile_queue,&sample,portMAX_DELAY)==pdTRUE) profile_log(&sample);
    }
}

static void profile_monitor(lv_disp_drv_t *driver,uint32_t elapsed_ms,uint32_t pixels)
{
    (void)driver;
    static unsigned frames;
    static unsigned intervals, over_35ms;
    static uint32_t slowest[12];
    static int64_t previous_end, longest_interval;
    int64_t now=esp_timer_get_time();
    int64_t interval=now-previous_end;
    if(previous_end && interval<250000) {
        intervals++;
        if(interval>35000) over_35ms++;
        if(interval>longest_interval) longest_interval=interval;
        for(unsigned i=0;i<sizeof(slowest)/sizeof(slowest[0]);i++) {
            if(interval<=slowest[i]) continue;
            uint32_t displaced=slowest[i];
            slowest[i]=(uint32_t)interval;
            interval=displaced;
        }
    }
    previous_end=now;
    if(++frames>3 && frames%120!=0) return;
    profile_sample_t sample={
        .frames=frames,.elapsed_ms=elapsed_ms,.pixels=pixels,.intervals=intervals,.over_35ms=over_35ms,
        .frame_end_us=now,.total_us=now-refresh_start,.draw_us=draw_us,.copy_us=copy_us,
        .flush_us=flush_us,.longest_us=longest_interval
    };
    memcpy(sample.slowest,slowest,sizeof(slowest));
    if(profile_queue) xQueueSend(profile_queue,&sample,0);
    intervals=over_35ms=0;
    longest_interval=0;
    memset(slowest,0,sizeof(slowest));
}

void display_profile_install(lv_disp_t *display)
{
    profile_queue=xQueueCreate(4,sizeof(profile_sample_t));
    if(profile_queue && xTaskCreatePinnedToCore(profile_log_task,"lcd_profile",3072,NULL,1,NULL,0)!=pdPASS) {
        vQueueDelete(profile_queue);
        profile_queue=NULL;
    }
    lv_disp_drv_t *driver=display->driver;
    lv_timer_t *timer=_lv_disp_get_refr_timer(display);
    original_refresh=timer->timer_cb;
    lv_timer_set_cb(timer,profile_refresh);
    original_flush=driver->flush_cb;
    driver->flush_cb=profile_flush;
    original_copy=driver->draw_ctx->buffer_copy;
    if(original_copy && driver->direct_mode) driver->draw_ctx->buffer_copy=profile_copy;
    driver->monitor_cb=profile_monitor;
}

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
    port_cfg.task_affinity = 1; // The NimBLE host and controller run on core 0.
    port_cfg.timer_period_ms = 2;
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
