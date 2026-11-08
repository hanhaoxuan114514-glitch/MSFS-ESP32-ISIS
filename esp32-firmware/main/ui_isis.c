#include "ui_isis.h"
#include "ui_scene.generated.h"
#include "flight_data.h"
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define WHITE 0xD9DCDA
#define GREEN 0x2CF525
#define RED 0xFF5555
#define CYAN 0x4DC9E5
#define SKY 0x2096D5
#define GROUND 0xB85948
#define BLACK 0x030609
#define DEG .017453292519943295f
typedef struct { float x, y; } point_t;
static const point_t boundary[] = {{92,68},{121,62},{163,58},{207,57},{250,59},{292,63},
    {321,69},{321,295},{291,302},{249,307},{207,309},{163,306},{121,301},{92,294}};
static flight_data_t data;
/* 与最后安排绘制的状态比较，使微小的连续变化能累积触发更新。 */
static flight_data_t presented;
static lv_obj_t *instrument;
static int sx(float x) { return (int)lroundf(240+(x-215)*1.25f); }
static int sy(float y) { return (int)lroundf(240+(y-221)*1.25f); }
static const lv_font_t *font(unsigned size)
{
    switch (size) {
    case 10: return &lv_font_montserrat_10; case 12: return &lv_font_montserrat_12;
    case 16: return &lv_font_montserrat_16; case 18: return &lv_font_montserrat_18;
    case 20: return &lv_font_montserrat_20; case 22: return &lv_font_montserrat_22;
    case 24: return &lv_font_montserrat_24; case 26: return &lv_font_montserrat_26;
    case 28: return &lv_font_montserrat_28; case 30: return &lv_font_montserrat_30;
    case 32: return &lv_font_montserrat_32; case 34: return &lv_font_montserrat_34;
    case 36: return &lv_font_montserrat_36; case 38: return &lv_font_montserrat_38;
    case 40: return &lv_font_montserrat_40; case 42: return &lv_font_montserrat_42;
    case 44: return &lv_font_montserrat_44; default: return &lv_font_montserrat_16;
    }
}
static void label(lv_draw_ctx_t *ctx,const char *text,float x,float y,float w,float h,unsigned size,uint32_t color,bool center)
{
    lv_area_t box={sx(x),sy(y),sx(x+w)-1,sy(y+h)-1},clip;
    if(!_lv_area_intersect(&clip,ctx->clip_area,&box)) return;
    lv_draw_label_dsc_t style;lv_draw_label_dsc_init(&style);
    style.font=font(size);style.color=lv_color_hex(color);
    style.align=center?LV_TEXT_ALIGN_CENTER:LV_TEXT_ALIGN_LEFT;style.flag=LV_TEXT_FLAG_EXPAND;
    const lv_area_t *old=ctx->clip_area;ctx->clip_area=&clip;
    lv_draw_label(ctx,&style,&box,text,NULL);ctx->clip_area=old;
}
static void line(lv_draw_ctx_t *ctx,point_t a,point_t b,uint32_t color,int width)
{
    lv_point_t p={sx(a.x),sy(a.y)},q={sx(b.x),sy(b.y)};
    lv_draw_line_dsc_t style;lv_draw_line_dsc_init(&style);style.color=lv_color_hex(color);style.width=width;
    lv_draw_line(ctx,&style,&p,&q);
}
static void centered_value(lv_draw_ctx_t *ctx,const char *text,float x,float y,float w,float h,unsigned size,uint32_t color)
{
    const lv_font_t *f=font(size);
    float offset=fmaxf(0,((h*1.25f)-f->line_height)/2)/1.25f;
    label(ctx,text,x,y+offset,w,h-offset,size,color,false);
}
static void polygon(lv_draw_ctx_t *ctx,const point_t *p,unsigned count,uint32_t color)
{
    if(count<3 || count>20) return;
    lv_point_t converted[20];
    for(unsigned i=0;i<count;i++) converted[i]=(lv_point_t){sx(p[i].x),sy(p[i].y)};
    lv_draw_rect_dsc_t style;lv_draw_rect_dsc_init(&style);
    style.bg_color=lv_color_hex(color);style.bg_opa=LV_OPA_COVER;style.border_width=0;
    lv_draw_polygon(ctx,&style,converted,count);
}
static float cross(point_t a,point_t b) {return a.x*b.y-a.y*b.x;}
static point_t sub(point_t a,point_t b) {return (point_t){a.x-b.x,a.y-b.y};}
/* 凸显示边界内裁剪线段，不把姿态刻度画到两侧数据带。 */
static bool clip_line(point_t *a,point_t *b)
{
    point_t direction=sub(*b,*a);float low=0,high=1;
    for(unsigned i=0;i<14;i++) {
        point_t e=sub(boundary[(i+1)%14],boundary[i]);
        float start=cross(e,sub(*a,boundary[i])),change=cross(e,direction);
        if(fabsf(change)<.0001f) {if(start<0) return false;else continue;}
        float t=-start/change;
        if(change>0) low=fmaxf(low,t);else high=fminf(high,t);
        if(low>high) return false;
    }
    point_t origin=*a;
    *a=(point_t){origin.x+low*direction.x,origin.y+low*direction.y};
    *b=(point_t){origin.x+high*direction.x,origin.y+high*direction.y};return true;
}
static point_t attitude_point(point_t p,float c,float s)
{
    return (point_t){207+c*(p.x-207)+s*(p.y-193),193-s*(p.x-207)+c*(p.y-193)};
}
static void attitude(lv_draw_ctx_t *ctx)
{
    polygon(ctx,boundary,14,data.connected?SKY:BLACK);
    if(!data.connected) return;
    float bank=(float)data.bank_deg*DEG,c=cosf(bank),s=sinf(bank),displacement=(float)data.pitch_deg*5.8f;
    /* 地面半平面：抬头使地平线下移，右滚使其逆时针。 */
    point_t ground[20];unsigned count=0;
    for(unsigned i=0;i<14;i++) {
        point_t a=boundary[i],b=boundary[(i+1)%14];
        float da=s*(a.x-207)+c*(a.y-193)-displacement;
        float db=s*(b.x-207)+c*(b.y-193)-displacement;
        if(da>=0) ground[count++]=a;
        if((da>=0)!=(db>=0)) {float t=da/(da-db);ground[count++]=(point_t){a.x+t*(b.x-a.x),a.y+t*(b.y-a.y)};}
    }
    polygon(ctx,ground,count,GROUND);
    point_t middle={207+s*displacement,193+c*displacement};
    point_t a={middle.x-600*c,middle.y+600*s},b={middle.x+600*c,middle.y-600*s};
    if(clip_line(&a,&b)) line(ctx,a,b,WHITE,3);
    for(int step=-36;step<=36;step++) {
        if(step==0) continue;
        float angle=step*2.5f,y=193+displacement-angle*5.8f;
        int half=step%4==0?24:step%2==0?11:7;
        a=attitude_point((point_t){207-half,y},c,s);b=attitude_point((point_t){207+half,y},c,s);
        if(clip_line(&a,&b)) {
            line(ctx,a,b,WHITE,3);
            if(step%4==0) {
                point_t text=attitude_point((point_t){146,y-14},c,s),end={text.x+30,text.y+28};
                point_t ta=text,tb=end;
                if(clip_line(&ta,&tb) && fabsf(ta.x-text.x)<.1f && fabsf(tb.y-end.y)<.1f) {
                    char number[8];snprintf(number,sizeof(number),"%d",abs(step*5/2));
                    label(ctx,number,text.x,text.y,30,28,28,WHITE,false);
                }
            }
        }
    }
    point_t pointer[3]={{207,82},{199,93},{215,93}};
    for(unsigned i=0;i<3;i++) {float x=pointer[i].x-207,y=pointer[i].y-193;pointer[i]=(point_t){207+c*x-s*y,193+s*x+c*y};}
    polygon(ctx,pointer,3,BLACK);
    for(unsigned i=0;i<3;i++) line(ctx,pointer[i],pointer[(i+1)%3],WHITE,3);
}
static void tapes(lv_draw_ctx_t *ctx)
{
    if(!data.connected) return;
    char value[16];int start=(int)floor((data.ias_kt-55)/10)*10;
    for(int speed=start;speed<=start+120;speed+=10) {
        float y=195-(speed-data.ias_kt)*2.6f;if(speed<0 || y<70 || y>294) continue;
        bool major=speed%20==10;
        line(ctx,(point_t){major?82:85,y},(point_t){94,y},WHITE,major?4:3);
        if(major && fabsf(y-195)>15) {snprintf(value,sizeof(value),"%d",speed);label(ctx,value,43,y-14,43,28,speed>999?22:30,WHITE,false);}
    }
    start=(int)floor((data.altitude_ft-700)/100)*100;
    for(int alt=start;alt<=start+1500;alt+=100) {
        float y=195-(alt-data.altitude_ft)*.168f;if(y<75 || y>294) continue;
        bool major=alt%500==0;
        line(ctx,(point_t){321,y},(point_t){major?339:331,y},WHITE,major?4:3);
        if(major && fabsf(y-195)>25) {snprintf(value,sizeof(value),"%03d",alt/100);label(ctx,value,334,y-15,58,30,strlen(value)>3?26:30,WHITE,false);}
    }
    start=(int)floor((data.heading_deg-30)/5)*5;
    for(int heading=start;heading<=start+65;heading+=5) {
        float delta=fmodf(heading-data.heading_deg+540,360)-180,x=201+delta*2.9f;
        if(x<133 || x>280) continue;
        int normalized=(heading%360+360)%360;
        line(ctx,(point_t){x,344},(point_t){x,normalized%10==0?332:338},WHITE,3);
        if(normalized%20==0 || normalized%90==0) {
            if(normalized%90==0) snprintf(value,sizeof(value),"%c","NESW"[normalized/90]);
            else snprintf(value,sizeof(value),"%d",normalized/10);
            label(ctx,value,x-20,312,40,26,28,WHITE,true);
        }
    }
}
static bool dynamic_role(unsigned role)
{
    return role!=ISIS_ROLE_STATIC && role!=ISIS_ROLE_BANK && role!=ISIS_ROLE_AIRCRAFT_CENTER;
}
static void fixed_scene(lv_draw_ctx_t *ctx)
{
    for(unsigned i=0;i<ISIS_COMMAND_COUNT;i++) {
        const isis_command_t *p=&isis_commands[i];if(dynamic_role(p->role)) continue;
        if(p->type==0) {
            lv_draw_rect_dsc_t d;lv_draw_rect_dsc_init(&d);d.bg_color=lv_color_hex(p->color);d.bg_opa=LV_OPA_COVER;d.border_width=0;
            lv_draw_polygon(ctx,&d,p->points,p->count);
        } else if(p->type==1) {
            lv_draw_line_dsc_t d;lv_draw_line_dsc_init(&d);d.color=lv_color_hex(p->color);d.width=p->width;
            for(unsigned j=1;j<p->count;j++) lv_draw_line(ctx,&d,&p->points[j-1],&p->points[j]);
        }
    }
}
static bool reversing(const flight_data_t *sample,unsigned engine)
{
    return sample->throttle_pct[engine]<-.5 || sample->reverse_pct[engine]>1;
}
static void engine_text(const flight_data_t *sample,unsigned engine,char *text,size_t size)
{
    if(!sample->connected || !sample->engine_valid[engine]) {snprintf(text,size,"--%%");return;}
    snprintf(text,size,"%s%.0f%%",reversing(sample,engine)?"REV ":"",fabs(sample->throttle_pct[engine]));
}
static void footer_text(const flight_data_t *sample,char *text,size_t size)
{
    char fuel[12],vs[16];
    if(sample->connected && sample->fuel_valid) snprintf(fuel,sizeof(fuel),"%.0f%%",sample->fuel_pct);else strcpy(fuel,"--%");
    double rounded=round(sample->vertical_speed_fpm/50)*50;
    if(sample->connected) snprintf(vs,sizeof(vs),"%+.0f",rounded==0?0:rounded);else strcpy(vs,"----");
    snprintf(text,size,"%s/%s",fuel,vs);
}
static uint32_t throttle_color(float percent)
{
    const uint32_t low=0x2CF525,mid=0xFFD341,high=0xFF5555;
    float t=percent<=50?percent/50:(percent-50)/50;
    uint32_t a=percent<=50?low:mid,b=percent<=50?mid:high,result=0;
    for(unsigned shift=0;shift<24;shift+=8) {
        float first=(a>>shift)&255,last=(b>>shift)&255;
        result|=(uint32_t)lroundf(first+(last-first)*t)<<shift;
    }
    return result;
}
static void progress_rect(lv_draw_ctx_t *ctx,float x,float y,float width,float height,uint32_t color)
{
    if(width<=0) return;
    lv_area_t area={sx(x),sy(y),sx(x+width)-1,sy(y+height)-1};
    lv_draw_rect_dsc_t style;lv_draw_rect_dsc_init(&style);
    style.bg_color=lv_color_hex(color);style.bg_opa=LV_OPA_COVER;style.border_width=0;style.radius=3;
    lv_draw_rect(ctx,&style,&area);
}
static void hud(lv_draw_ctx_t *ctx)
{
    char text[40];
    unsigned count=data.engine_count;
    if(count>4) count=4;
    float width=count?(336.f-(count-1)*8.f)/count:0;
    for(unsigned i=0;i<count;i++) {
        float x=47+i*(width+8),number_width=count>2?38:56;
        float bar_x=x+number_width+4,bar_width=width-number_width-4;
        bool valid=data.connected && data.engine_valid[i],rev=valid && reversing(&data,i);
        float percent=valid?(float)round(fabs(data.throttle_pct[i])):0;
        percent=fminf(100,fmaxf(0,percent));
        if(valid) snprintf(text,sizeof(text),"%.0f%%",percent);else strcpy(text,"--%");
        centered_value(ctx,text,x,366,number_width,22,count>2?16:22,WHITE);
        if(rev) label(ctx,"REV",x,355,number_width,12,10,0xE6D34C,false);
        progress_rect(ctx,bar_x,372,bar_width,12,0x26313B);
        if(valid) {
            progress_rect(ctx,bar_x,372,bar_width*percent/100,12,throttle_color(percent));
        }
    }
    if(data.connected && data.fuel_valid) snprintf(text,sizeof(text),"FUEL %.0f%%",data.fuel_pct);else strcpy(text,"FUEL --%");
    label(ctx,text,47,389,160,24,28,WHITE,true);
    double vs=round(data.vertical_speed_fpm/50)*50;
    if(data.connected) snprintf(text,sizeof(text),"V/S %+.0f",vs==0?0:vs);else strcpy(text,"V/S ----");
    label(ctx,text,223,389,160,24,28,WHITE,true);
}
static void numbers(lv_draw_ctx_t *ctx)
{
    char text[64];bool live=data.connected;
    if(live) snprintf(text,sizeof(text),"%.0f",data.ias_kt);else strcpy(text,"---");
    point_t box[]={{41,180},{87,180},{87,210},{41,210}};polygon(ctx,box,4,BLACK);
    label(ctx,text,43,181,43,28,strlen(text)>3?22:30,live?WHITE:RED,false);
    if(live) snprintf(text,sizeof(text),"%.0f",data.altitude_ft);else strcpy(text,"-----");
    centered_value(ctx,text,287,178,98,34,strlen(text)>5?30:36,live?GREEN:RED);
    if(live) snprintf(text,sizeof(text),"%.0f",data.qnh_hpa);else strcpy(text,"----");
    label(ctx,text,311,307,80,32,34,live?CYAN:RED,false);
    if(live && data.mach_valid) {snprintf(text,sizeof(text),"%.2f",data.mach);if(text[0]=='0') memmove(text,text+1,strlen(text));}
    else strcpy(text,"--");
    label(ctx,text,59,313,47,29,strlen(text)>3?24:30,GREEN,false);
    if(!live) label(ctx,"ATT",165,138,84,38,30,RED,true);
}
static void draw_isis(lv_event_t *event)
{
    lv_draw_ctx_t *ctx=lv_event_get_draw_ctx(event);attitude(ctx);tapes(ctx);fixed_scene(ctx);numbers(ctx);hud(ctx);
}
static void dirty(float x,float y,float w,float h)
{
    lv_area_t box={sx(x)-2,sy(y)-2,sx(x+w)+2,sy(y+h)+2};lv_obj_invalidate_area(instrument,&box);
}
static void refresh(lv_timer_t *timer)
{
    (void)timer;flight_data_t next;flight_data_snapshot(&next);
    bool full=next.connected!=data.connected || next.demo!=data.demo || next.mach_valid!=data.mach_valid;
    if(full) {data=next;presented=next;lv_obj_invalidate(instrument);return;}
    if(next.connected) {
        if(fabs(next.pitch_deg-presented.pitch_deg)>.01 || fabs(next.bank_deg-presented.bank_deg)>.01) {
            dirty(90,55,233,257);presented.pitch_deg=next.pitch_deg;presented.bank_deg=next.bank_deg;
        }
        if(fabs(next.ias_kt-presented.ias_kt)>.05) {dirty(40,69,72,232);presented.ias_kt=next.ias_kt;}
        if(fabs(next.altitude_ft-presented.altitude_ft)>.5) {dirty(278,70,114,231);presented.altitude_ft=next.altitude_ft;}
        if(fabs(next.heading_deg-presented.heading_deg)>.05) {dirty(131,309,151,50);presented.heading_deg=next.heading_deg;}
        if(fabs(next.qnh_hpa-presented.qnh_hpa)>.05) {dirty(310,306,82,34);presented.qnh_hpa=next.qnh_hpa;}
        if((int)round(next.mach*100)!=(int)round(presented.mach*100)) {dirty(58,312,49,31);presented.mach=next.mach;}
        char before[64],after[64];
        bool engines_dirty=next.engine_count!=presented.engine_count;
        for(unsigned i=0;i<4;i++) {
            engine_text(&presented,i,before,sizeof(before));engine_text(&next,i,after,sizeof(after));
            if(strcmp(before,after)) engines_dirty=true;
            presented.throttle_pct[i]=next.throttle_pct[i];presented.reverse_pct[i]=next.reverse_pct[i];presented.engine_valid[i]=next.engine_valid[i];
        }
        if(engines_dirty) dirty(45,354,340,36);
        presented.engine_count=next.engine_count;
        footer_text(&presented,before,sizeof(before));footer_text(&next,after,sizeof(after));
        if(strcmp(before,after)) {
            dirty(45,388,349,25);presented.fuel_pct=next.fuel_pct;presented.fuel_valid=next.fuel_valid;presented.vertical_speed_fpm=next.vertical_speed_fpm;
        }
    }
    data=next;
}
lv_obj_t *ui_isis_create(void)
{
    if(instrument) return instrument;
    lv_obj_t *screen=lv_scr_act();lv_obj_set_style_bg_color(screen,lv_color_hex(BLACK),0);
    lv_obj_set_style_bg_opa(screen,LV_OPA_COVER,0);lv_obj_clear_flag(screen,LV_OBJ_FLAG_SCROLLABLE);
    instrument=lv_obj_create(screen);lv_obj_remove_style_all(instrument);
    lv_obj_set_pos(instrument,0,0);lv_obj_set_size(instrument,480,480);lv_obj_clear_flag(instrument,LV_OBJ_FLAG_SCROLLABLE);
    lv_obj_add_event_cb(instrument,draw_isis,LV_EVENT_DRAW_MAIN,NULL);
    lv_timer_t *timer=lv_timer_create(refresh,50,NULL);
    if(!timer) {lv_obj_del(instrument);instrument=NULL;return NULL;}
    refresh(timer);return instrument;
}
