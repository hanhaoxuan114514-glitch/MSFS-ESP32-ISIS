#include "flight_data.h"

#include <math.h>
#include <stdio.h>
#include <string.h>
#include "cJSON.h"
#include "esp_timer.h"
#include "freertos/FreeRTOS.h"

static portMUX_TYPE s_lock = portMUX_INITIALIZER_UNLOCKED;
static flight_data_t s_data;
static char s_session[33];
static bool s_have_session;
static bool s_have_live_data;
static int64_t s_last_live_us;

static bool is_digit(char c)
{
    return c >= '0' && c <= '9';
}

static bool is_hex(char c)
{
    return is_digit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
}

/* cJSON 的数字解析允许 01、1.、1e 等非标准形式，先检查词法。
 * 结构仍交给 cJSON；限制嵌套深度，避免接收任务被恶意输入耗尽栈。 */
static bool strict_json_tokens(const char *s, size_t length)
{
    size_t i = 0;
    unsigned depth = 0;
    while (i < length) {
        char c = s[i++];
        if (c == ' ' || c == '\t' || c == '\r') {
            continue;
        }
        if (c == '{' || c == '[') {
            if (++depth > 8) {
                return false;
            }
        } else if (c == '}' || c == ']') {
            if (depth == 0) {
                return false;
            }
            --depth;
        } else if (c == ',' || c == ':') {
            continue;
        } else if (c == '"') {
            bool closed = false;
            while (i < length) {
                c = s[i++];
                if (c == '"') {
                    closed = true;
                    break;
                }
                if ((unsigned char)c < 0x20 || (unsigned char)c > 0x7e) {
                    return false;
                }
                if (c != '\\') {
                    continue;
                }
                if (i == length) {
                    return false;
                }
                c = s[i++];
                if (c == 'u') {
                    if (length - i < 4) {
                        return false;
                    }
                    bool nonzero = false;
                    for (size_t j = 0; j < 4; ++j) {
                        if (!is_hex(s[i + j])) {
                            return false;
                        }
                        nonzero |= s[i + j] != '0';
                    }
                    /* 防止 cJSON 字符串的 NUL 截断伪装字段名或会话。 */
                    if (!nonzero) {
                        return false;
                    }
                    i += 4;
                } else if (strchr("\"\\/bfnrt", c) == NULL) {
                    return false;
                }
            }
            if (!closed) {
                return false;
            }
        } else if (c == '-' || is_digit(c)) {
            size_t p = i - 1;
            if (s[p] == '-' && ++p == length) {
                return false;
            }
            if (s[p] == '0') {
                ++p;
            } else {
                if (s[p] < '1' || s[p] > '9') {
                    return false;
                }
                while (p < length && is_digit(s[p])) {
                    ++p;
                }
            }
            if (p < length && s[p] == '.') {
                ++p;
                size_t first = p;
                while (p < length && is_digit(s[p])) {
                    ++p;
                }
                if (p == first) {
                    return false;
                }
            }
            if (p < length && (s[p] == 'e' || s[p] == 'E')) {
                ++p;
                if (p < length && (s[p] == '+' || s[p] == '-')) {
                    ++p;
                }
                size_t first = p;
                while (p < length && is_digit(s[p])) {
                    ++p;
                }
                if (p == first) {
                    return false;
                }
            }
            if (p < length && strchr(" ,]}\t\r", s[p]) == NULL) {
                return false;
            }
            i = p;
        } else {
            const char *literal = c == 't' ? "true" : c == 'f' ? "false" : c == 'n' ? "null" : NULL;
            if (literal == NULL) {
                return false;
            }
            size_t n = strlen(literal);
            if (length - (i - 1) < n || memcmp(&s[i - 1], literal, n) != 0) {
                return false;
            }
            i += n - 1;
        }
    }
    return depth == 0;
}

/* 同时检查未知字段中的重复键和非有限数字，不能由扩展字段绕过约束。 */
static bool valid_tree(const cJSON *node)
{
    if (cJSON_IsNumber(node) && !isfinite(node->valuedouble)) {
        return false;
    }
    for (const cJSON *a = node->child; a != NULL; a = a->next) {
        if (cJSON_IsObject(node)) {
            for (const cJSON *b = a->next; b != NULL; b = b->next) {
                if (strcmp(a->string, b->string) == 0) {
                    return false;
                }
            }
        }
        if (!valid_tree(a)) {
            return false;
        }
    }
    return true;
}

static bool number(const cJSON *root, const char *key, double min, double max,
                   bool required, double *out)
{
    const cJSON *item = cJSON_GetObjectItemCaseSensitive(root, key);
    if (item == NULL) {
        return !required;
    }
    if (!cJSON_IsNumber(item) || !isfinite(item->valuedouble) ||
        item->valuedouble < min || item->valuedouble > max) {
        return false;
    }
    *out = item->valuedouble;
    return true;
}

static bool boolean(const cJSON *root, const char *key, bool required, bool *out)
{
    const cJSON *item = cJSON_GetObjectItemCaseSensitive(root, key);
    if (item == NULL) {
        return !required;
    }
    if (!cJSON_IsBool(item)) {
        return false;
    }
    *out = cJSON_IsTrue(item);
    return true;
}

static bool decode(const cJSON *root, flight_data_t *out, char session[33])
{
    double version = 0, seq = 0;
    const cJSON *id = cJSON_GetObjectItemCaseSensitive(root, "session");
    if (!number(root, "version", 1, 1, true, &version) ||
        !number(root, "seq", 0, UINT32_MAX, true, &seq) || floor(seq) != seq ||
        !boolean(root, "connected", true, &out->connected) ||
        !boolean(root, "demo", true, &out->demo) ||
        !cJSON_IsString(id) || strlen(id->valuestring) != 32) {
        return false;
    }
    for (size_t i = 0; i < 32; ++i) {
        char c = id->valuestring[i];
        if (!is_hex(c)) {
            return false;
        }
        /* 十六进制标识符不区分大小写，防止同一会话绕过序号检查。 */
        session[i] = c >= 'A' && c <= 'F' ? c + ('a' - 'A') : c;
    }
    session[32] = '\0';
    out->seq = (uint32_t)seq;
    bool required = out->connected;
    if (!number(root, "pitch_deg", -90, 90, required, &out->pitch_deg) ||
        !number(root, "bank_deg", -180, 180, required, &out->bank_deg) ||
        !number(root, "ias_kt", 0, 1500, required, &out->ias_kt) ||
        !number(root, "altitude_ft", -2000, 100000, required, &out->altitude_ft) ||
        !number(root, "heading_deg", 0, 360, required, &out->heading_deg) ||
        !number(root, "vertical_speed_fpm", -30000, 30000, required, &out->vertical_speed_fpm) ||
        !number(root, "qnh_hpa", 700, 1200, required, &out->qnh_hpa) ||
        !boolean(root, "on_ground", required, &out->on_ground) ||
        !number(root, "mach", 0, 5, false, &out->mach) ||
        !number(root, "latitude_deg", -90, 90, false, &out->latitude_deg) ||
        !number(root, "longitude_deg", -180, 180, false, &out->longitude_deg) ||
        !number(root, "fuel_pct", 0, 100, false, &out->fuel_pct)) {
        return false;
    }
    out->mach_valid = cJSON_GetObjectItemCaseSensitive(root, "mach") != NULL;
    double engines=2;
    if(!number(root,"engine_count",0,4,false,&engines) || floor(engines)!=engines) return false;
    out->engine_count=(uint8_t)engines;
    for(unsigned i=0;i<4;i++) {
        char throttle[24],reverse[24];
        snprintf(throttle,sizeof(throttle),"throttle%u_pct",i+1);snprintf(reverse,sizeof(reverse),"reverse%u_pct",i+1);
        if(!number(root,throttle,-100,100,false,&out->throttle_pct[i]) ||
           !number(root,reverse,0,100,false,&out->reverse_pct[i])) return false;
        out->engine_valid[i]=i<out->engine_count && cJSON_GetObjectItemCaseSensitive(root,throttle)!=NULL &&
                             cJSON_GetObjectItemCaseSensitive(root,reverse)!=NULL;
    }
    out->fuel_valid = cJSON_GetObjectItemCaseSensitive(root, "fuel_pct") != NULL;
    out->position_valid = cJSON_GetObjectItemCaseSensitive(root, "latitude_deg") != NULL &&
                          cJSON_GetObjectItemCaseSensitive(root, "longitude_deg") != NULL;
    if (out->heading_deg == 360) {
        out->heading_deg = 0;
    }
    return true;
}

void flight_data_init(void)
{
    portENTER_CRITICAL(&s_lock);
    memset(&s_data, 0, sizeof(s_data));
    memset(s_session, 0, sizeof(s_session));
    s_have_session = false;
    s_have_live_data = false;
    s_last_live_us = 0;
    portEXIT_CRITICAL(&s_lock);
}

bool flight_data_accept_line(const char *line, size_t length)
{
    if (line == NULL || length == 0 || length > FLIGHT_DATA_MAX_LINE_LENGTH ||
        memchr(line, '\0', length) != NULL || !strict_json_tokens(line, length)) {
        return false;
    }
    char text[FLIGHT_DATA_MAX_LINE_LENGTH + 1];
    memcpy(text, line, length);
    text[length] = '\0';
    /* 不使用全局错误指针；必须完全消费输入，拒绝尾随内容。 */
    cJSON *root = cJSON_ParseWithLengthOpts(text, length + 1, NULL, true);
    flight_data_t next = {0};
    char session[33];
    bool valid = root != NULL && cJSON_IsObject(root) && valid_tree(root) &&
                 decode(root, &next, session);
    cJSON_Delete(root);
    if (!valid) {
        return false;
    }

    bool accepted = false;
    portENTER_CRITICAL(&s_lock);
    int64_t now = esp_timer_get_time();
    bool same = s_have_session && strcmp(session, s_session) == 0;
    bool current_live = s_data.connected && s_have_live_data &&
                        now - s_last_live_us <= (int64_t)FLIGHT_DATA_TIMEOUT_MS * 1000;
    if ((!same && !current_live) || (same && next.seq > s_data.seq)) {
        memcpy(s_session, session, sizeof(s_session));
        s_have_session = true;
        if (next.connected) {
            s_data = next;
            s_last_live_us = now;
            s_have_live_data = true;
        } else {
            /* 状态帧不能覆盖真实读数，不能延长最后有效数据的寿命。 */
            s_data.connected = false;
            s_data.demo = next.demo;
            s_data.seq = next.seq;
        }
        accepted = true;
    }
    portEXIT_CRITICAL(&s_lock);
    return accepted;
}

bool flight_data_snapshot(flight_data_t *out)
{
    if (out == NULL) {
        return false;
    }
    int64_t last;
    bool have_live;
    portENTER_CRITICAL(&s_lock);
    *out = s_data;
    last = s_last_live_us;
    have_live = s_have_live_data;
    portEXIT_CRITICAL(&s_lock);
    uint64_t age_us = have_live ? (uint64_t)(esp_timer_get_time() - last) : UINT64_MAX;
    uint64_t age_ms = age_us / 1000;
    out->age_ms = age_ms > UINT32_MAX ? UINT32_MAX : (uint32_t)age_ms;
    out->connected = out->connected && have_live &&
                     age_us <= (uint64_t)FLIGHT_DATA_TIMEOUT_MS * 1000;
    return out->connected;
}
