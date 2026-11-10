#include "ble_receiver.h"
#include "flight_data.h"
#include "radar_data.h"
#include "esp_log.h"
#include "freertos/FreeRTOS.h"
#include "freertos/queue.h"
#include "freertos/task.h"
#include "host/ble_hs.h"
#include "host/util/util.h"
#include "nimble/nimble_port.h"
#include "nimble/nimble_port_freertos.h"
#include "nvs_flash.h"
#include "services/gap/ble_svc_gap.h"
#include "services/gatt/ble_svc_gatt.h"

static const ble_uuid128_t service_uuid = BLE_UUID128_INIT(
    0x10,0x57,0x14,0x42,0x24,0x97,0xe0,0x87,0x1e,0x40,0x30,0x6c,0x11,0x17,0x50,0xa3);
static const ble_uuid128_t telemetry_uuid = BLE_UUID128_INIT(
    0x10,0x57,0x14,0x42,0x24,0x97,0xe0,0x87,0x1e,0x40,0x30,0x6c,0x12,0x17,0x50,0xa3);
static const char name[] = "MyHud BLE";
static uint8_t address_type;
static QueueHandle_t latest_packet;

typedef struct {
    uint8_t bytes[244];
    uint16_t size;
} packet_t;

static int receive(uint16_t conn, uint16_t attr, struct ble_gatt_access_ctxt *ctx, void *arg)
{
    packet_t packet = {0};
    unsigned size = OS_MBUF_PKTLEN(ctx->om);
    if (size > sizeof(packet.bytes) || os_mbuf_copydata(ctx->om, 0, size, packet.bytes))
        return BLE_ATT_ERR_INVALID_ATTR_VALUE_LEN;
    packet.size = size;
    if(size && packet.bytes[0]==2)
        return radar_accept_packet(packet.bytes,size)?0:BLE_ATT_ERR_INVALID_ATTR_VALUE_LEN;
    xQueueOverwrite(latest_packet, &packet);
    return 0;
}

static const struct ble_gatt_svc_def services[] = {
    {.type = BLE_GATT_SVC_TYPE_PRIMARY, .uuid = &service_uuid.u,
     .characteristics = (struct ble_gatt_chr_def[]) {
         {.uuid = &telemetry_uuid.u, .access_cb = receive, .flags = BLE_GATT_CHR_F_WRITE_NO_RSP},
         {0}
     }},
    {0}
};

static void advertise(void);

static int gap_event(struct ble_gap_event *event, void *arg)
{
    if (event->type == BLE_GAP_EVENT_DISCONNECT ||
        (event->type == BLE_GAP_EVENT_CONNECT && event->connect.status != 0))
        advertise();
    return 0;
}

static void advertise(void)
{
    struct ble_hs_adv_fields fields = {
        .flags = BLE_HS_ADV_F_DISC_GEN | BLE_HS_ADV_F_BREDR_UNSUP,
        .name = (const uint8_t *)name, .name_len = sizeof(name) - 1, .name_is_complete = 1
    };
    struct ble_gap_adv_params params = {
        .conn_mode = BLE_GAP_CONN_MODE_UND, .disc_mode = BLE_GAP_DISC_MODE_GEN
    };
    if (!ble_gap_adv_set_fields(&fields))
        ble_gap_adv_start(address_type, NULL, BLE_HS_FOREVER, &params, gap_event, NULL);
}

static void on_sync(void)
{
    if (!ble_hs_id_infer_auto(0, &address_type)) advertise();
}

static void host_task(void *arg)
{
    nimble_port_run();
    nimble_port_freertos_deinit();
}

static void decode_task(void *arg)
{
    packet_t packet;
    for (;;) {
        xQueueReceive(latest_packet, &packet, portMAX_DELAY);
        flight_data_accept_ble(packet.bytes,packet.size);
    }
}

esp_err_t ble_receiver_start(void)
{
    esp_err_t result = nvs_flash_init();
    if (result != ESP_OK) return result;
    latest_packet = xQueueCreate(1, sizeof(packet_t));
    if (!latest_packet) return ESP_ERR_NO_MEM;
    result = nimble_port_init();
    if (result != ESP_OK) goto fail;
    esp_log_level_set("NimBLE", ESP_LOG_WARN);
    ble_svc_gap_init();
    ble_svc_gatt_init();
    ble_svc_gap_device_name_set(name);
    ble_hs_cfg.sync_cb = on_sync;
    if (ble_gatts_count_cfg(services) || ble_gatts_add_svcs(services) ||
        xTaskCreatePinnedToCore(decode_task, "ble_telemetry", 4096, NULL, 5, NULL, 0) != pdPASS) {
        nimble_port_deinit();
        result = ESP_FAIL;
        goto fail;
    }
    nimble_port_freertos_init(host_task);
    return ESP_OK;
fail:
    vQueueDelete(latest_packet);
    return result;
}
