idf_component_register(
    SRCS main.c display.c ui_isis.c flight_data.c serial_receiver.c
    INCLUDE_DIRS .
    REQUIRES waveshare__esp32_s3_touch_lcd_4 lvgl__lvgl
             espressif__esp_lvgl_port esp_psram esp_timer
             esp_driver_usb_serial_jtag json
             espressif__esp_io_expander_tca9554 espressif__esp_lcd_touch)
