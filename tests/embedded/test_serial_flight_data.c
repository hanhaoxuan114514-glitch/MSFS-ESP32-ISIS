#include <stdio.h>
#include <string.h>
#include "unity.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "flight_data.h"

/* 在独立 IDF Unity 测试程序中链接 flight_data.c、json、esp_timer。
 * 这些用例不安装 USB 驱动，也不使用任何串口。 */
#define SESSION_A "0123456789abcdef0123456789abcdef"
#define SESSION_B "fedcba9876543210fedcba9876543210"
#define LIVE_FIELDS "\"pitch_deg\":12.5,\"bank_deg\":-30,\"ias_kt\":245,\"altitude_ft\":12000,\"heading_deg\":360,\"vertical_speed_fpm\":1200,\"qnh_hpa\":1013,\"on_ground\":false"

static bool accept(const char *text)
{
    return flight_data_accept_line(text, strlen(text));
}

static bool live(const char *session, unsigned long seq, const char *extra)
{
    char line[1025];
    snprintf(line, sizeof(line), "{\"version\":1,\"session\":\"%s\",\"seq\":%lu,\"connected\":true,\"demo\":false," LIVE_FIELDS "%s}", session, seq, extra);
    return accept(line);
}

TEST_CASE("串口实时帧原子发布并规范航向", "[serial_data]")
{
    flight_data_t out;
    flight_data_init();
    TEST_ASSERT_FALSE(flight_data_snapshot(&out));
    TEST_ASSERT_EQUAL_UINT32(UINT32_MAX, out.age_ms);
    TEST_ASSERT_TRUE(live(SESSION_A, 0, ",\"mach\":0.72,\"latitude_deg\":51,\"longitude_deg\":-1"));
    TEST_ASSERT_TRUE(flight_data_snapshot(&out));
    TEST_ASSERT_EQUAL_DOUBLE(12.5, out.pitch_deg);
    TEST_ASSERT_EQUAL_DOUBLE(0, out.heading_deg);
    TEST_ASSERT_TRUE(out.mach_valid);
    TEST_ASSERT_TRUE(out.position_valid);
    TEST_ASSERT_TRUE(live(SESSION_A, 1, ",\"latitude_deg\":51"));
    TEST_ASSERT_TRUE(flight_data_snapshot(&out));
    TEST_ASSERT_FALSE(out.mach_valid);
    TEST_ASSERT_FALSE(out.position_valid);
}

TEST_CASE("断开立即生效并保留最后真实读数", "[serial_data]")
{
    flight_data_t out;
    flight_data_init();
    TEST_ASSERT_TRUE(live(SESSION_A, 2, ""));
    TEST_ASSERT_TRUE(accept("{\"version\":1,\"session\":\"" SESSION_A "\",\"seq\":3,\"connected\":false,\"demo\":false}"));
    TEST_ASSERT_FALSE(flight_data_snapshot(&out));
    TEST_ASSERT_EQUAL_DOUBLE(245, out.ias_kt);
    TEST_ASSERT_EQUAL_UINT32(3, out.seq);
    TEST_ASSERT_FALSE(live(SESSION_A, 2, ""));
    TEST_ASSERT_TRUE(live(SESSION_B, 0, ""));
    TEST_ASSERT_FALSE(live(SESSION_A, 4, ""));
}

TEST_CASE("辅助 HUD 可选字段边界和旧协议兼容", "[serial_data]")
{
    flight_data_t out;flight_data_init();
    TEST_ASSERT_TRUE(live(SESSION_A,0,",\"throttle1_pct\":65,\"reverse1_pct\":0,\"throttle2_pct\":-20,\"reverse2_pct\":100,\"fuel_pct\":25"));
    TEST_ASSERT_TRUE(flight_data_snapshot(&out));
    TEST_ASSERT_TRUE(out.engine_valid[0]);TEST_ASSERT_TRUE(out.engine_valid[1]);TEST_ASSERT_TRUE(out.fuel_valid);
    TEST_ASSERT_EQUAL_DOUBLE(-20,out.throttle_pct[1]);TEST_ASSERT_EQUAL_DOUBLE(25,out.fuel_pct);
    TEST_ASSERT_FALSE(live(SESSION_A,1,",\"fuel_pct\":101"));
    TEST_ASSERT_FALSE(live(SESSION_A,1,",\"reverse1_pct\":-1"));
    TEST_ASSERT_FALSE(live(SESSION_A,1,",\"throttle2_pct\":true"));
    TEST_ASSERT_TRUE(live(SESSION_A,1,""));TEST_ASSERT_TRUE(flight_data_snapshot(&out));
    TEST_ASSERT_FALSE(out.engine_valid[0]);TEST_ASSERT_FALSE(out.engine_valid[1]);TEST_ASSERT_FALSE(out.fuel_valid);
}

TEST_CASE("双发四发切换后不保留已隐藏发动机", "[serial_data]")
{
    flight_data_t out;flight_data_init();
    TEST_ASSERT_TRUE(live(SESSION_A,0,",\"engine_count\":4,\"throttle3_pct\":70,\"reverse3_pct\":0,\"throttle4_pct\":-20,\"reverse4_pct\":100"));
    TEST_ASSERT_TRUE(flight_data_snapshot(&out));TEST_ASSERT_EQUAL_UINT8(4,out.engine_count);
    TEST_ASSERT_TRUE(out.engine_valid[2]);TEST_ASSERT_TRUE(out.engine_valid[3]);TEST_ASSERT_EQUAL_DOUBLE(-20,out.throttle_pct[3]);
    TEST_ASSERT_FALSE(live(SESSION_A,1,",\"engine_count\":4.5"));
    TEST_ASSERT_TRUE(live(SESSION_A,1,",\"engine_count\":2"));TEST_ASSERT_TRUE(flight_data_snapshot(&out));
    TEST_ASSERT_EQUAL_UINT8(2,out.engine_count);TEST_ASSERT_FALSE(out.engine_valid[2]);TEST_ASSERT_FALSE(out.engine_valid[3]);
}

TEST_CASE("拒绝不合法帧且不改变已发布快照", "[serial_data]")
{
    const char *bad[] = {
        "{\"version\":1,\"session\":\"" SESSION_A "\",\"seq\":2,\"connected\":false,\"demo\":false,\"seq\":3}",
        "{\"version\":1,\"session\":\"" SESSION_A "\",\"seq\":2,\"connected\":true,\"demo\":false}",
        "{\"version\":1,\"session\":\"" SESSION_A "\",\"seq\":true,\"connected\":false,\"demo\":false}",
        "{\"version\":1,\"session\":\"" SESSION_A "\",\"seq\":-1,\"connected\":false,\"demo\":false}",
        "{\"version\":1,\"session\":\"" SESSION_A "\",\"seq\":4294967296,\"connected\":false,\"demo\":false}",
        "{\"version\":1,\"session\":\"" SESSION_A "\",\"seq\":2.5,\"connected\":false,\"demo\":false}",
        "{\"version\":1,\"session\":\"" SESSION_A "\",\"seq\":02,\"connected\":false,\"demo\":false}",
        "{\"version\":1,\"session\":\"" SESSION_A "\",\"seq\":2.,\"connected\":false,\"demo\":false}",
        "{\"version\":1,\"session\":\"" SESSION_A "\",\"seq\":2e,\"connected\":false,\"demo\":false}",
        "{\"version\":1,\"session\":\"" SESSION_A "\",\"seq\":2,\"connected\":false,\"demo\":false,\"extra\":1e999}",
        "{\"version\":1,\"session\":\"" SESSION_A "\",\"seq\":2,\"connected\":false,\"demo\":false}garbage",
    };
    flight_data_t out;
    flight_data_init();
    TEST_ASSERT_TRUE(live(SESSION_A, 1, ""));
    for (size_t i = 0; i < sizeof(bad) / sizeof(bad[0]); ++i) {
        TEST_ASSERT_FALSE_MESSAGE(accept(bad[i]), bad[i]);
        TEST_ASSERT_TRUE(flight_data_snapshot(&out));
        TEST_ASSERT_EQUAL_UINT32(1, out.seq);
        TEST_ASSERT_EQUAL_DOUBLE(245, out.ias_kt);
    }
    TEST_ASSERT_FALSE(live(SESSION_A, 2, ",\"mach\":true"));
    TEST_ASSERT_FALSE(live(SESSION_A, 2, ",\"mach\":6"));
    TEST_ASSERT_FALSE(live(SESSION_A, 2, ",\"latitude_deg\":91"));
    TEST_ASSERT_FALSE(live(SESSION_A, 2, ",\"pitch_deg\":0"));
    TEST_ASSERT_FALSE(live(SESSION_A, 2, ",\"p\\u0069tch_deg\":0"));
}

TEST_CASE("超时允许新会话但旧序列不能延长有效期", "[serial_data]")
{
    flight_data_t out;
    flight_data_init();
    TEST_ASSERT_TRUE(live(SESSION_A, 20, ""));
    TEST_ASSERT_FALSE(live(SESSION_B, 0, ""));
    vTaskDelay(pdMS_TO_TICKS(800));
    TEST_ASSERT_FALSE(live(SESSION_A, 20, ""));
    vTaskDelay(pdMS_TO_TICKS(800));
    TEST_ASSERT_FALSE(flight_data_snapshot(&out));
    TEST_ASSERT_GREATER_THAN_UINT32(1500, out.age_ms);
    TEST_ASSERT_FALSE(live(SESSION_A, 19, ""));
    TEST_ASSERT_TRUE(live(SESSION_B, 0, ""));
}

TEST_CASE("序号不回绕且长度与空字符受限", "[serial_data]")
{
    char too_long[1026];
    memset(too_long, ' ', sizeof(too_long));
    flight_data_init();
    TEST_ASSERT_FALSE(flight_data_accept_line(NULL, 1));
    TEST_ASSERT_FALSE(flight_data_accept_line("", 0));
    TEST_ASSERT_FALSE(flight_data_accept_line(too_long, sizeof(too_long)));
    TEST_ASSERT_FALSE(flight_data_accept_line("{}\0{}", 5));
    TEST_ASSERT_TRUE(live(SESSION_A, 4294967295UL, ""));
    TEST_ASSERT_FALSE(live(SESSION_A, 0, ""));
}
