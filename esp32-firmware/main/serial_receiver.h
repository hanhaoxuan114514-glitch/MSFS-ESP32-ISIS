#pragma once

#include "esp_err.h"

/* app_main 初始化数据模型后调用；重复成功调用不会再创建任务。
 * 独占 USB 的接收方向；保留 UART 主日志和 USB 次日志的既有路由。 */
esp_err_t serial_receiver_start(void);
