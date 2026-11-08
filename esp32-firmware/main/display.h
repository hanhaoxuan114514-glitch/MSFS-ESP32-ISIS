#pragma once
#include "lvgl.h"

/* V3 无触摸版本：只启动 LCD，不注册 GT911。 */
lv_disp_t *display_start_lcd_only(void);
