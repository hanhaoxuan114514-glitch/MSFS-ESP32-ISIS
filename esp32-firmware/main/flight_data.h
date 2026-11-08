#pragma once

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#define FLIGHT_DATA_MAX_LINE_LENGTH 1024
#define FLIGHT_DATA_TIMEOUT_MS 1500

typedef struct {
    bool connected, demo, on_ground;
    bool mach_valid, position_valid;
    bool engine_valid[4], fuel_valid;
    uint8_t engine_count;
    uint32_t seq, age_ms;
    double pitch_deg, bank_deg, ias_kt, altitude_ft, heading_deg;
    double vertical_speed_fpm, qnh_hpa, mach, latitude_deg, longitude_deg;
    double throttle_pct[4], reverse_pct[4], fuel_pct;
} flight_data_t;

/* 启动接收任务前调用；重复调用会清除会话与最后数据。 */
void flight_data_init(void);

/* 输入不含换行；验证全部完成后发布，不持锁解析或分配内存。 */
bool flight_data_accept_line(const char *line, size_t length);

/* out 必须非空；始终填写快照，返回值为连接且最后真实数据未过期。
 * 尚无真实数据时 age_ms 为 UINT32_MAX；失联时保留最后读数供诊断。 */
bool flight_data_snapshot(flight_data_t *out);
