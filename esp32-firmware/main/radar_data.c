#include "radar_data.h"
#include "esp_timer.h"
#include "freertos/FreeRTOS.h"
#include <string.h>

#define HEADER_BYTES 16
#define TARGET_BYTES 14

static portMUX_TYPE lock=portMUX_INITIALIZER_UNLOCKED;
static radar_data_t current;
static int64_t last_packet;

static int16_t read_i16(const uint8_t *value)
{
    int value16=value[0] | (value[1]<<8);
    return (int16_t)(value16>=32768?value16-65536:value16);
}

static void copy_label(char *destination,const uint8_t *source,size_t size)
{
    for(size_t i=0;i<size && source[i];i++)
        destination[i]=source[i]>=32 && source[i]<=126?(char)source[i]:'?';
}

bool radar_accept_packet(const uint8_t *packet,size_t size)
{
    if(!packet || size<HEADER_BYTES || packet[0]!=2 || packet[1]>3 ||
       (packet[1] && (packet[2]<5 || packet[2]>100)) ||
       packet[3]>RADAR_MAX_TARGETS || size!=HEADER_BYTES+packet[3]*TARGET_BYTES ||
       (packet[1]!=2 && packet[3]!=0)) return false;
    radar_data_t next={0};
    next.status=packet[1];
    next.range_nm=packet[2];
    next.count=packet[3];
    copy_label(next.site,packet+4,12);
    for(unsigned i=0;i<next.count;i++) {
        const uint8_t *p=packet+HEADER_BYTES+i*TARGET_BYTES;
        radar_target_t *target=&next.targets[i];
        target->east=read_i16(p);
        target->north=read_i16(p+2);
        if(target->east < -1000 || target->east > 1000 || target->north < -1000 || target->north > 1000)
            return false;
        target->bearing=p[4];
        target->altitude_hundreds=read_i16(p+5);
        copy_label(target->callsign,p+7,7);
    }
    portENTER_CRITICAL(&lock);
    current=next;
    last_packet=esp_timer_get_time();
    portEXIT_CRITICAL(&lock);
    return true;
}

static int hex_digit(char value)
{
    if(value>='0' && value<='9') return value-'0';
    if(value>='a' && value<='f') return value-'a'+10;
    return -1;
}

bool radar_accept_line(const char *line,size_t size)
{
    uint8_t packet[HEADER_BYTES+RADAR_MAX_TARGETS*TARGET_BYTES];
    if(size<6 || memcmp(line,"RADAR ",6)!=0 || (size-6)%2 || (size-6)/2>sizeof(packet))
        return false;
    size_t bytes=(size-6)/2;
    for(size_t i=0;i<bytes;i++) {
        int high=hex_digit(line[6+i*2]),low=hex_digit(line[7+i*2]);
        if(high<0 || low<0) return false;
        packet[i]=(uint8_t)(high*16+low);
    }
    return radar_accept_packet(packet,bytes);
}

void radar_snapshot(radar_data_t *out)
{
    portENTER_CRITICAL(&lock);
    *out=current;
    int64_t age=esp_timer_get_time()-last_packet;
    portEXIT_CRITICAL(&lock);
    if(out->status && age>5000000) {
        out->status=3;
        out->count=0;
    }
}
