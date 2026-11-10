#pragma once

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#define FLIGHT_DATA_MAX_LINE_LENGTH 1024
#define FLIGHT_DATA_TIMEOUT_MS 1500

typedef enum {
    AIRCRAFT_JET, AIRCRAFT_PROP, AIRCRAFT_HELI, AIRCRAFT_GLIDER,
    AIRCRAFT_AIRSHIP, AIRCRAFT_BALLOON, AIRCRAFT_LTA, AIRCRAFT_GYRO,
    AIRCRAFT_TILT, AIRCRAFT_ORNITHOPTER, AIRCRAFT_OTHER, AIRCRAFT_ELECTRIC
} aircraft_kind_t;

typedef enum {
    DETAIL_AOA, DETAIL_TRIM, DETAIL_FLAP_ANGLE, DETAIL_ROTOR1_RPM, DETAIL_ROTOR2_RPM,
    DETAIL_ROTOR2_PCT, DETAIL_POWER1, DETAIL_POWER2, DETAIL_POWER3, DETAIL_POWER4, DETAIL_COUNT
} hud_detail_t;

typedef struct {
    bool connected, demo, on_ground;
    bool parking_brake_valid, parking_brake, wheel_brake;
    bool mach_valid, position_valid;
    bool tas_valid, g_load_valid;
    bool propeller, agl_valid;
    bool ground_speed_valid, rotor_valid, collective_valid, wind_valid;
    bool prop_rpm_valid[4], prop_rpm_pct_valid[4];
    bool engine_valid[4], fuel_valid;
    uint8_t engine_count;
    uint8_t rotor_count;
    uint16_t detail_valid;
    double details[DETAIL_COUNT];
    aircraft_kind_t aircraft_kind;
    uint8_t flaps_index, flaps_positions;
    uint32_t seq, age_ms;
    double pitch_deg, bank_deg, ias_kt, altitude_ft, heading_deg;
    double tas_kt, g_load;
    double vs0_kt, flap_limit_kt, overspeed_kt, agl_ft;
    double ground_speed_kt, rotor_pct, collective_pct, wind_kt;
    double prop_rpm[4], prop_rpm_pct[4];
    bool vertical_speed_valid;
    double vertical_speed_fpm, qnh_hpa, mach, latitude_deg, longitude_deg;
    double throttle_pct[4], reverse_pct[4], fuel_pct;
} flight_data_t;

/* 启动接收任务前调用；重复调用会清除会话与最后数据。 */
void flight_data_init(void);

/* 输入不含换行；验证全部完成后发布，不持锁解析或分配内存。 */
bool flight_data_accept_line(const char *line, size_t length);
bool flight_data_accept_ble(const uint8_t *packet, size_t length);

/* out 必须非空；始终填写快照，返回值为连接且最后真实数据未过期。
 * 尚无真实数据时 age_ms 为 UINT32_MAX；失联时保留最后读数供诊断。 */
bool flight_data_snapshot(flight_data_t *out);
