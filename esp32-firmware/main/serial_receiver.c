#include "serial_receiver.h"

#include <stdbool.h>
#include <stdatomic.h>
#include "driver/usb_serial_jtag.h"
#include "esp_log.h"
#include "flight_data.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"

static const char *TAG = "serial_rx";
/* 0 未启动，1 正在启动，2 已启动；不持有临界区调用驱动或创建任务。 */
static atomic_int s_state;

static void receiver_task(void *arg)
{
    (void)arg;
    char line[FLIGHT_DATA_MAX_LINE_LENGTH + 1];
    unsigned char chunk[256];
    size_t used = 0;
    bool discard = false;
    for (;;) {
        int count = usb_serial_jtag_read_bytes(chunk, sizeof(chunk), pdMS_TO_TICKS(100));
        for (int i = 0; i < count; ++i) {
            unsigned char c = chunk[i];
            if (c == '\n') {
                if (!discard && used != 0) {
                    line[used] = '\0';
                    flight_data_accept_line(line, used);
                }
                used = 0;
                discard = false;
            } else if (!discard) {
                if (used == FLIGHT_DATA_MAX_LINE_LENGTH) {
                    /* 超长后必须等到换行，不能把尾部识别成另一帧。 */
                    discard = true;
                    used = 0;
                } else {
                    line[used++] = (char)c;
                }
            }
        }
        if (count < 0) {
            /* 防止底层意外报错导致空转；不按包打印干扰日志链路。 */
            vTaskDelay(pdMS_TO_TICKS(100));
        }
    }
}

esp_err_t serial_receiver_start(void)
{
    int expected = 0;
    if (!atomic_compare_exchange_strong(&s_state, &expected, 1)) {
        return expected == 2 ? ESP_OK : ESP_ERR_INVALID_STATE;
    }
    esp_err_t err = ESP_OK;
    if (!usb_serial_jtag_is_driver_installed()) {
        usb_serial_jtag_driver_config_t config = {
            .tx_buffer_size = 256,
            .rx_buffer_size = 4096,
        };
        err = usb_serial_jtag_driver_install(&config);
        /* 别处恰好完成安装时复用驱动，不卸载、不重配现有控制台。 */
        if (err == ESP_ERR_INVALID_STATE && usb_serial_jtag_is_driver_installed()) {
            err = ESP_OK;
        }
    }
    if (err == ESP_OK && xTaskCreate(receiver_task, "flight_usb_rx", 6144, NULL,
                                    tskIDLE_PRIORITY + 2, NULL) != pdPASS) {
        err = ESP_ERR_NO_MEM;
    }
    /* 即使创建任务失败也保留已安装驱动，避免改变共享 USB 日志状态。 */
    atomic_store(&s_state, err == ESP_OK ? 2 : 0);
    if (err == ESP_OK) {
        ESP_LOGI(TAG, "USB Serial/JTAG receiver ready");
    } else {
        ESP_LOGE(TAG, "Receiver start failed: %s", esp_err_to_name(err));
    }
    return err;
}
