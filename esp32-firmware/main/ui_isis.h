#pragma once
#include "lvgl.h"

/* USB 数据实时驱动；未连接时明确显示 NO DATA。调用方持有 LVGL 锁。 */
lv_obj_t *ui_isis_create(void);
