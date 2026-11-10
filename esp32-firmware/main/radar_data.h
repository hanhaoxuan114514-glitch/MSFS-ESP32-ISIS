#pragma once
#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#define RADAR_MAX_TARGETS 12

typedef struct {
    int16_t east, north, altitude_hundreds;
    uint8_t bearing;
    char callsign[8];
} radar_target_t;

typedef struct {
    uint8_t status, range_nm, count;
    char site[13];
    radar_target_t targets[RADAR_MAX_TARGETS];
} radar_data_t;

bool radar_accept_packet(const uint8_t *packet, size_t size);
bool radar_accept_line(const char *line, size_t size);
void radar_snapshot(radar_data_t *out);
