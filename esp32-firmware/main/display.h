#pragma once
#include "lvgl.h"

/* V3 无触摸版本：只启动 LCD，不注册 GT911。 */
lv_disp_t *display_start_lcd_only(void);
void display_profile_install(lv_disp_t *display);
void display_profile_add_draw(int64_t elapsed_us);
