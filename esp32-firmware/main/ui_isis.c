#include "ui_isis.h"
#include "ui_scene.generated.h"
#include "flight_data.h"
#include "radar_data.h"
#include "display.h"
#include "esp_timer.h"
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
#define MACH_X 39
#define MACH_Y 310
#define MACH_W 72
#define MACH_H 27
typedef struct { float x, y; } point_t;
/* Mirror the contour about x=207 and y=183. */
static const point_t boundary[] = {{92,69},{120,62},{163,58},{207,57},{251,58},{294,62},
    {322,69},{322,297},{294,304},{251,308},{207,309},{163,308},{120,304},{92,297}};
static flight_data_t data;
static radar_data_t radar_view;
/* 与最后安排绘制的状态比较，使微小的连续变化能累积触发更新。 */
static flight_data_t presented;
static int speed_arrow, altitude_arrow;
static double speed_sample, speed_rate;
static uint32_t speed_sample_tick;
static uint32_t tape_sample_tick;
static uint32_t altitude_sample_tick;
static bool mode_transition;
static aircraft_kind_t aircraft_layout, next_aircraft_layout;
static int32_t transition_phase;
static lv_obj_t *instrument;
static struct {
    bool active;
    uint32_t seq, tick, interval;
    double pitch, bank, pitch_delta, bank_delta;
} pose;

static void smooth_pose(flight_data_t *sample)
{
    if(!sample->connected) {
        pose.active=false;
        return;
    }
    if(!pose.active || sample->seq!=pose.seq) {
        uint32_t tick=lv_tick_get()-sample->age_ms;
        uint32_t interval=tick-pose.tick;
        double pitch_delta=sample->pitch_deg-pose.pitch;
        double bank_delta=remainder(sample->bank_deg-pose.bank,360);
        bool continuous=pose.active && sample->seq>pose.seq && interval>=12 && interval<=200 &&
                        fabs(pitch_delta)<=30 && fabs(bank_delta)<=55;
        pose.interval=continuous?interval:0;
        pose.pitch_delta=continuous?pitch_delta:0;
        pose.bank_delta=continuous?bank_delta:0;
        pose.pitch=sample->pitch_deg;
        pose.bank=sample->bank_deg;
        pose.seq=sample->seq;
        pose.tick=tick;
        pose.active=true;
    }
    if(pose.interval) {
        // Interpolate behind the latest sample. Brief gaps use bounded prediction.
        double offset=fmax(-(double)pose.interval,fmin(65,(double)sample->age_ms-45))/pose.interval;
        double pitch=pose.pitch_delta*offset,bank=pose.bank_delta*offset;
        if(offset>0) {
            pitch=fmax(-8,fmin(8,pitch));
            bank=fmax(-15,fmin(15,bank));
        }
        sample->pitch_deg=fmax(-90,fmin(90,pose.pitch+pitch));
        sample->bank_deg=remainder(pose.bank+bank,360);
    }
}
static bool rotor_layout(void)
{
    return aircraft_layout==AIRCRAFT_HELI || aircraft_layout==AIRCRAFT_GYRO || aircraft_layout==AIRCRAFT_TILT;
}
static bool rpm_layout(void)
{
    return aircraft_layout==AIRCRAFT_PROP || aircraft_layout==AIRCRAFT_AIRSHIP;
}
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
static void solid_area(lv_draw_ctx_t *ctx,lv_area_t area,uint32_t color)
{
    lv_area_t clip;
    if(!_lv_area_intersect(&clip,ctx->clip_area,&area) ||
       !_lv_area_intersect(&clip,&clip,ctx->buf_area)) return;
    int stride=lv_area_get_width(ctx->buf_area);
    lv_color_t *row=(lv_color_t *)ctx->buf+(clip.y1-ctx->buf_area->y1)*stride+clip.x1-ctx->buf_area->x1;
    lv_color_t fill=lv_color_hex(color);
    for(int y=0;y<=clip.y2-clip.y1;y++)
        lv_color_fill(row+y*stride,fill,lv_area_get_width(&clip));
}
static void solid_rect(lv_draw_ctx_t *ctx,float x,float y,float w,float h,uint32_t color)
{
    solid_area(ctx,(lv_area_t){sx(x),sy(y),sx(x+w)-1,sy(y+h)-1},color);
}
static long mach_display(const flight_data_t *sample)
{
    return sample->connected && sample->mach_valid?lround(sample->mach*100):-1;
}
static void mach_text(const flight_data_t *sample,char *out,size_t size,bool prefix)
{
    long value=mach_display(sample);
    if(value<0) snprintf(out,size,"%s--",prefix?"M ":"");
    else if(prefix || value>=100) snprintf(out,size,"%s%ld.%02ld",prefix?"M ":"",value/100,value%100);
    else snprintf(out,size,".%02ld",value);
}
static void centered_value(lv_draw_ctx_t *ctx,const char *text,float x,float y,float w,float h,unsigned size,uint32_t color)
{
    const lv_font_t *f=font(size);
    float offset=fmaxf(0,((h*1.25f)-f->line_height)/2)/1.25f;
    label(ctx,text,x,y+offset,w,h-offset,size,color,false);
}
static void detail_label(lv_draw_ctx_t *ctx,const char *text,float x,float width)
{
    label(ctx,text,x,37,width,18,16,WHITE,true);
    label(ctx,text,x+.8f,37,width,18,16,WHITE,true);
}
static float percent_value(lv_draw_ctx_t *ctx,float anchor,float y,float height,
                          unsigned size,double percent,bool valid,bool align_right,bool show_unit)
{
    char value[8];
    if(valid) snprintf(value,sizeof(value),"%ld",lround(percent));
    else strcpy(value,"--");

    unsigned unit_size=size>16?12:10;
    const lv_font_t *digits=font(size),*unit=font(unit_size);
    float number_width=lv_txt_get_width(value,strlen(value),digits,0,LV_TEXT_FLAG_NONE)/1.25f;
    bool draw_unit=valid && show_unit;
    float unit_width=draw_unit?lv_txt_get_width("%",1,unit,0,LV_TEXT_FLAG_NONE)/1.25f:0;
    float gap=draw_unit?1.6f:0;
    float left=align_right?anchor-number_width-gap-unit_width:anchor;
    float top=y+fmaxf(0,(height*1.25f-digits->line_height)/2)/1.25f;
    label(ctx,value,left,top,number_width,height-(top-y),size,WHITE,false);
    if(draw_unit) {
        float unit_top=top+(digits->line_height-digits->base_line-unit->line_height+unit->base_line)/1.25f;
        label(ctx,"%",left+number_width+gap,unit_top,unit_width,unit->line_height/1.25f,unit_size,0x8FA1AF,false);
    }
    return left+number_width+gap+unit_width;
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

static void draw_backdrop(lv_draw_ctx_t *ctx,bool show_attitude,float c,float s,float displacement)
{
    static int16_t left[480],right[480];
    static lv_color_t sky_row[480],ground_row[480],black_row[480];
    static bool ready;
    if(!ready) {
        for(int y=sy(57);y<sy(309);y++) {
            float low=480,high=0;
            for(unsigned i=0;i<14;i++) {
                point_t a=boundary[i],b=boundary[(i+1)%14];
                int ay=sy(a.y),by=sy(b.y);
                float scan=y+.5f;
                if((ay<=scan && scan<by) || (by<=scan && scan<ay)) {
                    float x=sx(a.x)+(sx(b.x)-sx(a.x))*(scan-ay)/(by-ay);
                    low=fminf(low,x);high=fmaxf(high,x);
                }
            }
            left[y]=(int16_t)ceilf(low-.5f);
            right[y]=(int16_t)ceilf(high-.5f)-1;
        }
        lv_color_fill(sky_row,lv_color_hex(SKY),480);
        lv_color_fill(ground_row,lv_color_hex(GROUND),480);
        lv_color_fill(black_row,lv_color_hex(BLACK),480);
        ready=true;
    }

    lv_area_t clip;
    if(!_lv_area_intersect(&clip,ctx->clip_area,ctx->buf_area)) return;
    const int stride=lv_area_get_width(ctx->buf_area);
    const int first=sy(57),last=sy(309)-1;
    const float center_x=sx(207),center_y=sy(193);
    const float height=displacement*1.25f;
    // Fill every clipped pixel exactly once, including outside the attitude window.
    for(int y=clip.y1;y<=clip.y2;y++) {
        lv_color_t *row=(lv_color_t *)ctx->buf+(y-ctx->buf_area->y1)*stride+clip.x1-ctx->buf_area->x1;
        if(!show_attitude || y<first || y>last) {
            memcpy(row,black_row,lv_area_get_width(&clip)*sizeof(*row));
            continue;
        }
        int x1=LV_MAX(clip.x1,left[y]),x2=LV_MIN(clip.x2,right[y]);
        if(x1>x2) {
            memcpy(row,black_row,lv_area_get_width(&clip)*sizeof(*row));
            continue;
        }
        if(x1>clip.x1) memcpy(row,black_row,(x1-clip.x1)*sizeof(*row));
        if(x2<clip.x2) memcpy(row+x2+1-clip.x1,black_row,(clip.x2-x2)*sizeof(*row));
        row+=x1-clip.x1;
        float distance=c*(y+.5f-center_y)-height;
        float edge1=distance+s*(x1+.5f-center_x),edge2=distance+s*(x2+.5f-center_x);
        if((edge1>=0)==(edge2>=0)) {
            memcpy(row,edge1>=0?ground_row:sky_row,(x2-x1+1)*sizeof(*row));
        } else {
            int split=(int)ceilf(center_x-.5f-distance/s);
            if(s<0) split=(int)floorf(center_x-.5f-distance/s)+1;
            split=LV_MAX(x1,LV_MIN(split,x2+1));
            if(split>x1) memcpy(row,edge1>=0?ground_row:sky_row,(split-x1)*sizeof(*row));
            if(split<=x2) memcpy(row+split-x1,edge2>=0?ground_row:sky_row,(x2-split+1)*sizeof(*row));
        }
    }
}

/* Three-pixel attitude strokes use clipped RGB spans with antialiased side edges. */
static void attitude_stroke(lv_draw_ctx_t *ctx,point_t a,point_t b)
{
    lv_area_t clip;
    if(!_lv_area_intersect(&clip,ctx->clip_area,ctx->buf_area)) return;
    int x0=sx(a.x),x1=sx(b.x),y0=sy(a.y),y1=sy(b.y);
    if(LV_MAX(x0,x1)+3<clip.x1 || LV_MIN(x0,x1)-3>clip.x2 ||
       LV_MAX(y0,y1)+3<clip.y1 || LV_MIN(y0,y1)-3>clip.y2) return;
    bool steep=abs(y1-y0)>abs(x1-x0);
    int m0=steep?y0:x0,m1=steep?y1:x1,n0=steep?x0:y0,n1=steep?x1:y1;
    if(m0>m1) {
        int swap=m0;m0=m1;m1=swap;
        swap=n0;n0=n1;n1=swap;
    }
    if(m0==m1) return;
    const int major_min=LV_MAX(m0,steep?clip.y1:clip.x1);
    const int major_max=LV_MIN(m1,steep?clip.y2:clip.x2);
    const int minor_min=steep?clip.x1:clip.y1,minor_max=steep?clip.x2:clip.y2;
    const int stride=lv_area_get_width(ctx->buf_area);
    const float slope=(float)(n1-n0)/(m1-m0);
    const int step=(int)lroundf(slope*256);
    const int radius=(int)lroundf(384*sqrtf(1+slope*slope));
    // Keep subpixel coverage identical when the same line is split across dirty areas.
    int center=n0*256+(major_min-m0)*step;
    const lv_color_t ink=lv_color_hex(WHITE);
    for(int major=major_min;major<=major_max;major++,center+=step) {
        const int low=center-radius,high=center+radius;
        const int first=LV_MAX(minor_min,(low+128)>>8);
        const int last=LV_MIN(minor_max,(high+127)>>8);
        for(int minor=first;minor<=last;minor++) {
            const int coverage=LV_MIN(high,minor*256+128)-LV_MAX(low,minor*256-128);
            if(coverage<=0) continue;
            const int x=steep?minor:major,y=steep?major:minor;
            lv_color_t *pixel=(lv_color_t *)ctx->buf+(y-ctx->buf_area->y1)*stride+x-ctx->buf_area->x1;
            *pixel=coverage>=255?ink:lv_color_mix(ink,*pixel,(lv_opa_t)coverage);
        }
    }
}

static void attitude(lv_draw_ctx_t *ctx,float c,float s,float displacement)
{
    const lv_area_t *area=ctx->clip_area;
    if(area->x2<sx(92)-2 || area->x1>sx(322)+2 || area->y2<sy(57)-2 || area->y1>sy(309)+2)
        return;
    if(!data.connected) return;
    float low=INFINITY,high=-INFINITY;
    for(unsigned i=0;i<14;i++) {
        point_t p=boundary[i];
        float projected=s*(p.x-207)+c*(p.y-193);
        low=fminf(low,projected);high=fmaxf(high,projected);
    }
    int first=LV_MAX(-36,(int)ceilf((displacement-high)/14.5f));
    int last=LV_MIN(36,(int)floorf((displacement-low)/14.5f));
    const lv_area_t *original_clip=ctx->clip_area;
    lv_area_t ladder_clip=*original_clip;
    if(ladder_clip.y1<sy(116)) ladder_clip.y1=sy(116);
    if(ladder_clip.y1<=ladder_clip.y2) {
        ctx->clip_area=&ladder_clip;
        point_t middle={207+s*displacement,193+c*displacement};
        point_t a={middle.x-600*c,middle.y+600*s},b={middle.x+600*c,middle.y-600*s};
        if(clip_line(&a,&b)) attitude_stroke(ctx,a,b);
        for(int step=first;step<=last;step++) {
            if(step==0) continue;
            float angle=step*2.5f,y=193+displacement-angle*5.8f;
            int half=step%4==0?24:step%2==0?11:7;
            a=attitude_point((point_t){207-half,y},c,s);b=attitude_point((point_t){207+half,y},c,s);
            if(clip_line(&a,&b)) {
                attitude_stroke(ctx,a,b);
                if(step%4==0 && fabs(data.bank_deg)<=20) {
                    point_t text=attitude_point((point_t){146,y-14},c,s),end={text.x+30,text.y+28};
                    point_t ta=text,tb=end;
                    if(clip_line(&ta,&tb) && fabsf(ta.x-text.x)<.1f && fabsf(tb.y-end.y)<.1f) {
                        char number[8];snprintf(number,sizeof(number),"%d",abs(step*5/2));
                        label(ctx,number,text.x,text.y,30,24,22,WHITE,false);
                    }
                }
            }
        }
    }
    ctx->clip_area=original_clip;
    float pointer_angle=fmaxf(-30,fminf(30,(float)data.bank_deg))*DEG;
    float pointer_c=cosf(pointer_angle),pointer_s=sinf(pointer_angle);
    point_t pointer[3]={{207,87},{201,97},{213,97}};
    for(unsigned i=0;i<3;i++) {
        float x=pointer[i].x-207,y=pointer[i].y-197;
        pointer[i]=(point_t){207+pointer_c*x-pointer_s*y,197+pointer_s*x+pointer_c*y};
    }
    polygon(ctx,pointer,3,BLACK);
    for(unsigned i=0;i<3;i++) line(ctx,pointer[i],pointer[(i+1)%3],WHITE,3);
}
static void speed_band(lv_draw_ctx_t *ctx,double minimum,double maximum,uint32_t color)
{
    float top=fmaxf(70,195-(maximum-data.ias_kt)*2.6);
    float bottom=fminf(rpm_layout()?247:294,195-(minimum-data.ias_kt)*2.6);
    if(bottom<=top) return;
    lv_area_t band={sx(92),sy(top),sx(color==RED?103:100)-1,sy(bottom)-1};
    lv_draw_rect_dsc_t style;
    lv_draw_rect_dsc_init(&style);
    style.bg_color=lv_color_hex(color);
    style.bg_opa=LV_OPA_COVER;
    style.border_width=0;
    lv_draw_rect(ctx,&style,&band);
}
static void tapes(lv_draw_ctx_t *ctx)
{
    if(!data.connected) return;
    const lv_area_t *clip=ctx->clip_area;
    char value[16];
    if(clip->x1<=sx(130) && clip->y1<=sy(306) && clip->y2>=sy(59)) {
        if(data.flap_limit_kt>0) {
            double upper=data.overspeed_kt>0?fmin(data.flap_limit_kt,data.overspeed_kt):data.flap_limit_kt;
            speed_band(ctx,data.vs0_kt,upper,WHITE);
        }
        if(data.flap_limit_kt<=0 && data.vs0_kt>0) {
            float y=195-(data.vs0_kt-data.ias_kt)*2.6f;
            float bottom=rpm_layout()?247:294;
            y=fminf(bottom-7,fmaxf(72,y));
            line(ctx,(point_t){92,y},(point_t){100,y},WHITE,4);
            label(ctx,"VS0",103,y-6,26,12,10,WHITE,false);
        }
        if(data.overspeed_kt>0) speed_band(ctx,data.overspeed_kt,1600,RED);
        int start=(int)floor((data.ias_kt-55)/10)*10;
        for(int speed=start;speed<=start+120;speed+=10) {
            float y=195-(speed-data.ias_kt)*2.6f;if(speed<0 || y<70 || y>(rpm_layout()?247:294)) continue;
            bool major=speed%20==10;
            line(ctx,(point_t){major?82:85,y},(point_t){94,y},WHITE,major?4:3);
            if(major && fabsf(y-195)>18 && (!rpm_layout() || y<=235)) {
                snprintf(value,sizeof(value),"%d",speed);
                label(ctx,value,43,y-11,36,22,speed>999?16:22,WHITE,false);
            }
        }
    }
    if(clip->x2>=sx(320) && clip->y1<=sy(311) && clip->y2>=sy(59)) {
        int start=(int)floor((data.altitude_ft-700)/100)*100;
        for(int alt=start;alt<=start+1500;alt+=100) {
            float y=195-(alt-data.altitude_ft)*.168f;if(y<75 || y>294) continue;
            bool major=alt%500==0;
            line(ctx,(point_t){322,y},(point_t){major?331:327,y},WHITE,major?4:3);
            if(major && fabsf(y-195)>25) {snprintf(value,sizeof(value),"%03d",alt/100);label(ctx,value,334,y-15,58,30,strlen(value)>3?26:30,WHITE,false);}
        }
    }
    if(clip->y1>sy(346) || clip->y2<sy(311)) return;
    int start=(int)floor((data.heading_deg-30)/5)*5;
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
            const lv_point_t *a=p->points;
            if(p->count==4 && a[0].y==a[1].y && a[1].x==a[2].x && a[2].y==a[3].y && a[3].x==a[0].x) {
                solid_area(ctx,(lv_area_t){LV_MIN(a[0].x,a[2].x),LV_MIN(a[0].y,a[2].y),
                    LV_MAX(a[0].x,a[2].x),LV_MAX(a[0].y,a[2].y)},p->color);
                continue;
            }
            lv_draw_rect_dsc_t d;lv_draw_rect_dsc_init(&d);d.bg_color=lv_color_hex(p->color);d.bg_opa=LV_OPA_COVER;d.border_width=0;
            lv_draw_polygon(ctx,&d,p->points,p->count);
        } else if(p->type==1) {
            lv_draw_line_dsc_t d;lv_draw_line_dsc_init(&d);d.color=lv_color_hex(p->color);d.width=p->width;
            for(unsigned j=1;j<p->count;j++) {
                lv_point_t a=p->points[j-1],b=p->points[j];
                if(a.x==b.x || a.y==b.y) {
                    // Solid, overlapping ends keep orthogonal reticle joins pixel-tight.
                    int before=p->width/2,after=(p->width-1)/2;
                    solid_area(ctx,(lv_area_t){LV_MIN(a.x,b.x)-before,LV_MIN(a.y,b.y)-before,
                        LV_MAX(a.x,b.x)+after,LV_MAX(a.y,b.y)+after},p->color);
                } else lv_draw_line(ctx,&d,&a,&b);
            }
        }
    }
}
static bool reversing(const flight_data_t *sample,unsigned engine)
{
    return sample->throttle_pct[engine]<-.5 || sample->reverse_pct[engine]>1;
}
static void engine_text(const flight_data_t *sample,unsigned engine,char *text,size_t size)
{
    if(!sample->connected || !sample->engine_valid[engine]) {snprintf(text,size,"--");return;}
    snprintf(text,size,"%s%.0f",reversing(sample,engine)?"REV ":"",fabs(sample->throttle_pct[engine]));
}
static long vertical_speed_display(const flight_data_t *sample)
{
    return lround(sample->vertical_speed_fpm/50)*50;
}
static void trend_arrow(lv_draw_ctx_t *ctx,float x,int span)
{
    if(!span) return;
    float tip=195-span, shoulder=tip+(span>0?7:-7);
    line(ctx,(point_t){x,195},(point_t){x,tip},WHITE,4);
    line(ctx,(point_t){x-5,shoulder},(point_t){x,tip},WHITE,4);
    line(ctx,(point_t){x+5,shoulder},(point_t){x,tip},WHITE,4);
}
static int trend_span(double displacement)
{
    if(fabs(displacement)<3) return 0;
    int length=(int)lround(fmin(70,fmax(10,fabs(displacement))));
    return displacement<0?-length:length;
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
static bool detail_available(hud_detail_t detail)
{
    return data.connected && (data.detail_valid & (1u<<detail));
}

static void parking_brake_badge(lv_draw_ctx_t *ctx)
{
    bool valid=data.connected && data.parking_brake_valid;
    uint32_t color=valid?(data.parking_brake?RED:data.wheel_brake?GROUND:GREEN):0x425865;
    progress_rect(ctx,28,393,40,18,color);
    progress_rect(ctx,29,394,38,16,BLACK);
    label(ctx,valid && !data.parking_brake && data.wheel_brake?"BRK":"PARK",
          29,395,38,15,12,valid?color:0x8FA1AF,true);
}

static void rotor_hud(lv_draw_ctx_t *ctx)
{
    char text[40];
    unsigned engines=data.engine_count;
    float power_width=engines?(336.f-8*(engines-1))/engines:0;
    for(unsigned i=0;i<engines;i++) {
        float x=47+i*(power_width+8);
        hud_detail_t field=DETAIL_POWER1+i;
        bool valid=detail_available(field);
        double power=data.details[field];
        if(valid) snprintf(text,sizeof(text),"PWR %u  %.0f",i+1,power);
        else snprintf(text,sizeof(text),"PWR %u  --",i+1);
        label(ctx,text,x,354,power_width,14,12,WHITE,true);
        progress_rect(ctx,x,369,power_width,5,0x26313B);
        if(valid) progress_rect(ctx,x,369,power_width*fmin(100,power)/100,5,power>100?RED:GREEN);
    }
    unsigned rotors=data.rotor_count?data.rotor_count:1;
    for(unsigned i=0;i<rotors;i++) {
        float x=47+i*172, width=rotors==1?336:164;
        hud_detail_t rpm_field=DETAIL_ROTOR1_RPM+i;
        bool valid=i?detail_available(DETAIL_ROTOR2_PCT):data.connected && data.rotor_valid;
        double pct=i?data.details[DETAIL_ROTOR2_PCT]:data.rotor_pct;
        char rpm[12],percent[12];
        if(detail_available(rpm_field)) snprintf(rpm,sizeof(rpm),"%.0f",data.details[rpm_field]);
        else strcpy(rpm,"--");
        if(valid) snprintf(percent,sizeof(percent),"%.0f%%",pct);
        else strcpy(percent,"--");
        snprintf(text,sizeof(text),"R%u  %s RPM  %s",i+1,rpm,percent);
        label(ctx,text,x,376,width,15,12,WHITE,true);
        progress_rect(ctx,x,391,width,5,0x26313B);
        if(valid) progress_rect(ctx,x,391,width*fmin(100,pct)/100,5,pct>100?RED:GREEN);
    }
    parking_brake_badge(ctx);
    label(ctx,"FUEL",74,398,37,15,12,WHITE,false);
    bool fuel=data.connected && data.fuel_valid;
    percent_value(ctx,113,397,16,12,data.fuel_pct,fuel,false,false);
    progress_rect(ctx,140,400,68,9,0x26313B);
    if(fuel) progress_rect(ctx,140,400,68*data.fuel_pct/100,9,GREEN);
    label(ctx,"COL",223,398,39,15,12,WHITE,false);
    bool collective=data.connected && data.collective_valid;
    percent_value(ctx,266,397,16,12,data.collective_pct,collective,false,true);
    progress_rect(ctx,307,400,76,9,0x26313B);
    if(collective) progress_rect(ctx,307,400,76*data.collective_pct/100,9,CYAN);
}

static void hud(lv_draw_ctx_t *ctx)
{
    if(rotor_layout()) {rotor_hud(ctx);return;}
    char text[40];
    unsigned count=data.engine_count;
    if(count>4) count=4;
    float width=count?(336.f-(count-1)*8.f)/count:0;
    for(unsigned i=0;i<count;i++) {
        float x=47+i*(width+8),number_width=count>2?30:42;
        float bar_x=x+number_width+3,bar_width=width-number_width-3;
        bool valid=data.connected && data.engine_valid[i],rev=valid && reversing(&data,i);
        float percent=valid?(float)round(fabs(data.throttle_pct[i])):0;
        percent=fminf(100,fmaxf(0,percent));
        percent_value(ctx,x+number_width,366,22,count>2?16:22,percent,valid,true,false);
        if(rev) {
            float rev_width=lv_txt_get_width("REV",3,font(10),0,LV_TEXT_FLAG_NONE)/1.25f;
            label(ctx,"REV",x+number_width-rev_width,355,rev_width,12,10,0xE6D34C,false);
        }
        progress_rect(ctx,bar_x,372,bar_width,12,0x26313B);
        if(valid) {
            progress_rect(ctx,bar_x,372,bar_width*percent/100,12,throttle_color(percent));
        }
    }
    if(aircraft_layout>=AIRCRAFT_HELI && aircraft_layout!=AIRCRAFT_AIRSHIP) {
        if(data.connected && data.ground_speed_valid) snprintf(text,sizeof(text),"GS %.0f KT",data.ground_speed_kt);
        else strcpy(text,"GS -- KT");
        label(ctx,text,53,392,150,21,18,WHITE,true);
        if(data.connected && data.wind_valid) snprintf(text,sizeof(text),"WIND %.0f KT",data.wind_kt);
        else strcpy(text,"WIND -- KT");
        label(ctx,text,217,392,165,21,18,WHITE,true);
        return;
    }
    bool fuel_valid=data.connected && data.fuel_valid;
    parking_brake_badge(ctx);
    const float fuel_bar_x=73, flap_bar_x=272, bar_width=85;
    float fuel_label_width=lv_txt_get_width("FUEL",4,font(12),0,LV_TEXT_FLAG_NONE)/1.25f;
    float fuel_number_end=percent_value(ctx,fuel_bar_x+bar_width+3,392,21,16,data.fuel_pct,fuel_valid,false,false);
    float fuel_label_x=fuel_number_end+3;
    centered_value(ctx,"FUEL",fuel_label_x,392,fuel_label_width,21,12,WHITE);
    progress_rect(ctx,fuel_bar_x,396,bar_width,12,0x26313B);
    if(fuel_valid) progress_rect(ctx,fuel_bar_x,396,bar_width*data.fuel_pct/100,12,GREEN);

    if(aircraft_layout==AIRCRAFT_AIRSHIP) {
        if(data.connected && data.ground_speed_valid) snprintf(text,sizeof(text),"GS %.0f KT",data.ground_speed_kt);
        else strcpy(text,"GS -- KT");
        label(ctx,text,220,392,164,21,18,WHITE,true);
        return;
    }
    unsigned positions=data.connected?data.flaps_positions:0;
    if(positions) snprintf(text,sizeof(text),"FLAP %u",(unsigned)data.flaps_index);else strcpy(text,"FLAP --");
    float flap_label_width=lv_txt_get_width(text,strlen(text),font(12),0,LV_TEXT_FLAG_NONE)/1.25f;
    centered_value(ctx,text,flap_bar_x-3-flap_label_width,392,flap_label_width,21,12,WHITE);
    if(positions) {
        float slot=bar_width/positions;
        for(unsigned i=0;i<positions;i++)
            progress_rect(ctx,flap_bar_x+i*slot,396,i+1==positions?slot:slot-1.8f,12,i==data.flaps_index?CYAN:0x425865);
    } else {
        progress_rect(ctx,flap_bar_x,396,bar_width,12,0x26313B);
    }
}
static void numbers(lv_draw_ctx_t *ctx)
{
    char text[64];bool live=data.connected;
    if(live) snprintf(text,sizeof(text),"%.0f",data.ias_kt);else strcpy(text,"---");
    solid_rect(ctx,41,180,46,30,BLACK);
    bool overspeed=live && data.overspeed_kt>0 && data.ias_kt>=data.overspeed_kt;
    label(ctx,text,43,181,43,28,strlen(text)>3?22:30,live && !overspeed?WHITE:RED,false);
    if(live) snprintf(text,sizeof(text),"%.0f",data.altitude_ft);else strcpy(text,"-----");
    centered_value(ctx,text,287,178,98,34,strlen(text)>5?30:36,live?GREEN:RED);
    if(live) snprintf(text,sizeof(text),"%.0f",data.qnh_hpa);else strcpy(text,"----");
    label(ctx,text,311,307,80,32,34,live?CYAN:RED,false);
    if(live && data.vertical_speed_valid) snprintf(text,sizeof(text),"V/S %+ld",vertical_speed_display(&data));
    else strcpy(text,"V/S ----");
    label(ctx,text,294,336,98,18,strlen(text)>10?16:18,WHITE,true);
    if(rotor_layout()) {
        if(live && data.ground_speed_valid) snprintf(text,sizeof(text),"GS %.0f KT",data.ground_speed_kt);
        else strcpy(text,"GS -- KT");
        label(ctx,text,40,313,92,23,20,WHITE,false);
    } else if(!rpm_layout()) {
        mach_text(&data,text,sizeof(text),false);
        solid_rect(ctx,MACH_X,MACH_Y,MACH_W,MACH_H,BLACK);
        label(ctx,text,MACH_X,MACH_Y,MACH_W,MACH_H,28,GREEN,true);
    } else {
        label(ctx,aircraft_layout==AIRCRAFT_AIRSHIP?"ENG RPM":"PROP RPM",34,249,59,12,10,WHITE,false);
        for(unsigned i=0;i<data.engine_count;i++) {
            float y=263+17*i;
            if(live && data.prop_rpm_valid[i])
                snprintf(text,sizeof(text),"%u %.0f",i+1,data.prop_rpm[i]);
            else snprintf(text,sizeof(text),"%u --",i+1);
            label(ctx,text,34,y,36,13,10,WHITE,false);
            progress_rect(ctx,72,y+2,20,8,0x26313B);
            if(live && data.prop_rpm_pct_valid[i]) {
                float fill=(float)fmin(100,data.prop_rpm_pct[i])/5;
                progress_rect(ctx,72,y+2,fill,8,data.prop_rpm_pct[i]>100?RED:GREEN);
            }
        }
    }
    if(aircraft_layout==AIRCRAFT_PROP) {
        if(detail_available(DETAIL_AOA)) snprintf(text,sizeof(text),"AOA %+ld\xC2\xB0",lround(data.details[DETAIL_AOA]));
        else strcpy(text,"AOA --");
        detail_label(ctx,text,85,70);
        if(detail_available(DETAIL_TRIM)) snprintf(text,sizeof(text),"TRIM %+.0f%%",data.details[DETAIL_TRIM]);
        else strcpy(text,"TRIM --");
        detail_label(ctx,text,157,78);
        if(detail_available(DETAIL_FLAP_ANGLE)) snprintf(text,sizeof(text),"FLAP %.0f\xC2\xB0",data.details[DETAIL_FLAP_ANGLE]);
        else strcpy(text,"FLAP --");
        detail_label(ctx,text,237,67);
    }
    if(live && data.agl_valid) snprintf(text,sizeof(text),"AGL %.0f FT",data.agl_ft);
    else strcpy(text,"AGL -- FT");
    if(aircraft_layout==AIRCRAFT_PROP) detail_label(ctx,text,305,101);
    else detail_label(ctx,text,287,105);
    if(live && data.tas_valid) snprintf(text,sizeof(text),"TAS %.0f KT",data.tas_kt);else strcpy(text,"TAS -- KT");
    label(ctx,text,40,339,90,16,16,WHITE,false);
    if(!live) label(ctx,"ATT",165,138,84,38,30,RED,true);
}
static void balloon_card(lv_draw_ctx_t *ctx,float x,float y,float width,const char *caption,
                         const char *unit,double value,bool valid)
{
    char text[24];
    progress_rect(ctx,x,y,width,76,0x121D27);
    label(ctx,caption,x,y+7,width,15,12,CYAN,true);
    if(valid) snprintf(text,sizeof(text),"%.0f",value);
    else strcpy(text,"--");
    label(ctx,text,x,y+(unit?24:30),width,37,36,WHITE,true);
    if(unit) label(ctx,unit,x,y+59,width,14,12,WHITE,true);
}

static void balloon_hud(lv_draw_ctx_t *ctx)
{
    label(ctx,"BALLOON",39,46,352,30,28,WHITE,true);
    balloon_card(ctx,39,91,171,"ALTITUDE","FT",data.altitude_ft,data.connected);
    balloon_card(ctx,220,91,171,"ABOVE GROUND","FT",data.agl_ft,data.connected && data.agl_valid);
    balloon_card(ctx,39,179,352,"VERTICAL SPEED","FT/MIN",data.vertical_speed_fpm,
                 data.connected && data.vertical_speed_valid);
    balloon_card(ctx,39,267,171,"GROUND SPEED","KT",data.ground_speed_kt,data.connected && data.ground_speed_valid);
    balloon_card(ctx,220,267,171,"FUEL",NULL,data.fuel_pct,data.connected && data.fuel_valid);
}

#define CURTAIN_CLOSED 350
#define CURTAIN_OPENING 650
#define DIAGONAL_END 958

static int sweep_edge(int32_t phase)
{
    bool opening=phase>=CURTAIN_OPENING;
    float t=opening?(phase-CURTAIN_OPENING)/(float)(1000-CURTAIN_OPENING):
                    phase/(float)CURTAIN_CLOSED;
    t=fminf(1,fmaxf(0,t));
    return (int)lroundf(DIAGONAL_END*t*t*(3-2*t));
}

static bool curtain_title_visible(int32_t phase)
{
    return phase>=CURTAIN_CLOSED && phase<CURTAIN_OPENING;
}

static bool curtain_covers_clip(const lv_area_t *clip)
{
    int edge=sweep_edge(transition_phase);
    if(transition_phase>=CURTAIN_OPENING) return clip->x1+clip->y1>edge;
    return clip->x2+clip->y2<=edge;
}

static void draw_curtain(lv_draw_ctx_t *ctx)
{
    bool opening=transition_phase>=CURTAIN_OPENING;
    int edge=sweep_edge(transition_phase);
    int extent=opening?DIAGONAL_END-edge:edge;
    lv_draw_rect_dsc_t shade;
    lv_draw_rect_dsc_init(&shade);
    shade.bg_color=lv_color_hex(0);
    shade.bg_opa=LV_OPA_COVER;
    shade.border_width=0;
    if(extent>=DIAGONAL_END) {
        lv_area_t screen={0,0,479,479};
        lv_draw_rect(ctx,&shade,&screen);
    } else if(extent>0) {
        lv_point_t points[5]={{0,0}};
        unsigned count;
        if(extent<=479) {
            points[1]=(lv_point_t){extent,0};
            points[2]=(lv_point_t){0,extent};
            count=3;
        } else {
            points[1]=(lv_point_t){479,0};
            points[2]=(lv_point_t){479,extent-479};
            points[3]=(lv_point_t){extent-479,479};
            points[4]=(lv_point_t){0,479};
            count=5;
        }
        if(opening) {
            for(unsigned i=0;i<count;i++) {
                points[i].x=479-points[i].x;
                points[i].y=479-points[i].y;
            }
        }
        lv_draw_polygon(ctx,&shade,points,count);
    }
    if(curtain_title_visible(transition_phase)) {
        static const char *const titles[]={
            "JET","PROP","HELI","GLIDER","AIRSHIP","BALLOON",
            "LTA","GYRO","TILTROTOR","ORNITHOPTER","AIRCRAFT","ELECTRIC"
        };
        label(ctx,titles[next_aircraft_layout],30,204,370,45,44,WHITE,true);
    }
}

#define RADAR_X 215
#define RADAR_Y 221
#define RADAR_RADIUS 126

typedef struct {
    int x,y,w,h;
    char altitude[16];
} radar_tag_t;
static radar_tag_t radar_tags[RADAR_MAX_TARGETS];
static unsigned radar_symbols_only;

static point_t radar_point(const radar_target_t *target)
{
    return (point_t){RADAR_X+target->east*RADAR_RADIUS/1000.f,
                     RADAR_Y-target->north*RADAR_RADIUS/1000.f};
}
static bool radar_tag_overlap(const radar_tag_t *a,const radar_tag_t *b)
{
    return b->w && a->x<b->x+b->w && b->x<a->x+a->w &&
                   a->y<b->y+b->h && b->y<a->y+a->h;
}
static bool place_radar_tag(radar_tag_t *tag,point_t point,const radar_tag_t *occupied,unsigned count)
{
    int x=(int)lroundf(point.x),y=(int)lroundf(point.y);
    const int positions[][2]={{x+10,y-tag->h/2},{x-tag->w-10,y-tag->h/2},
        {x-tag->w/2,y-tag->h-10},{x-tag->w/2,y+10},
        {x+10,y-tag->h-8},{x-tag->w-10,y-tag->h-8},{x+10,y+8},{x-tag->w-10,y+8}};
    for(unsigned i=0;i<sizeof(positions)/sizeof(positions[0]);i++) {
        tag->x=positions[i][0];tag->y=positions[i][1];
        if(tag->x<28 || tag->x+tag->w>402 || tag->y<88 || tag->y+tag->h>356) continue;
        bool fits=true;
        for(unsigned j=0;j<count;j++) if(radar_tag_overlap(tag,&occupied[j])) {fits=false;break;}
        if(fits) return true;
    }
    return false;
}
static void layout_radar_tags(void)
{
    radar_tag_t occupied[2*RADAR_MAX_TARGETS+2]={
        {.x=50,.y=211,.w=23,.h=21},{.x=357,.y=211,.w=23,.h=21}
    };
    unsigned count=2;
    for(unsigned i=0;i<radar_view.count;i++) {
        point_t p=radar_point(&radar_view.targets[i]);
        occupied[count++]=(radar_tag_t){.x=lroundf(p.x)-8,.y=lroundf(p.y)-8,.w=17,.h=17};
    }
    memset(radar_tags,0,sizeof(radar_tags));
    radar_symbols_only=0;
    for(unsigned i=0;i<radar_view.count;i++) {
        const radar_target_t *target=&radar_view.targets[i];
        radar_tag_t tag={0};
        if(target->altitude_hundreds!=INT16_MIN)
            snprintf(tag.altitude,sizeof(tag.altitude),"%d FT",target->altitude_hundreds*100);
        int name=lv_txt_get_width(target->callsign,strlen(target->callsign),font(18),0,LV_TEXT_FLAG_NONE);
        int altitude=lv_txt_get_width(tag.altitude,strlen(tag.altitude),font(16),0,LV_TEXT_FLAG_NONE);
        if(!name && !altitude) {radar_symbols_only++;continue;}
        tag.w=(int)ceilf(LV_MAX(name,altitude)/1.25f)+8;
        tag.h=name && altitude?36:20;
        point_t p=radar_point(target);
        bool placed=place_radar_tag(&tag,p,occupied,count);
        if(!placed && name && altitude) {
            // A readable callsign takes priority over an overlapping second line.
            tag.altitude[0]=0;tag.w=(int)ceilf(name/1.25f)+8;tag.h=20;
            placed=place_radar_tag(&tag,p,occupied,count);
        }
        if(placed) {radar_tags[i]=tag;occupied[count++]=tag;}
        else radar_symbols_only++;
    }
}

static void draw_live_radar(lv_draw_ctx_t *ctx)
{
    const float x=RADAR_X,y=RADAR_Y,radius=RADAR_RADIUS;
    const uint32_t grid=0x29483B,track=0x9FE3AD;
    draw_backdrop(ctx,false,1,0,0);
    char text[32];
    if(data.connected) snprintf(text,sizeof(text),"ALT %.0f FT",data.altitude_ft);
    else strcpy(text,"ALT -- FT");
    label(ctx,text,27,34,123,24,18,WHITE,true);
    if(data.connected) snprintf(text,sizeof(text),"IAS %.0f KT",data.ias_kt);
    else strcpy(text,"IAS -- KT");
    label(ctx,text,151,32,128,23,18,WHITE,true);
    mach_text(&data,text,sizeof(text),true);
    label(ctx,text,151,54,128,21,18,CYAN,true);
    if(data.connected && data.fuel_valid) snprintf(text,sizeof(text),"FUEL %.0f",data.fuel_pct);
    else strcpy(text,"FUEL --");
    label(ctx,text,280,34,123,24,18,WHITE,true);
    line(ctx,(point_t){29,77},(point_t){401,77},grid,1);
    for(unsigned i=1;i<=3;i++) {
        float r=radius*i/3;
        lv_area_t ring={sx(x-r),sy(y-r),sx(x+r),sy(y+r)};
        lv_draw_rect_dsc_t style;
        lv_draw_rect_dsc_init(&style);
        style.bg_opa=LV_OPA_TRANSP;
        style.radius=LV_RADIUS_CIRCLE;
        style.border_color=lv_color_hex(grid);
        style.border_width=1;
        lv_draw_rect(ctx,&style,&ring);
    }
    line(ctx,(point_t){x-radius,y},(point_t){x+radius,y},grid,1);
    line(ctx,(point_t){x,y-radius},(point_t){x,y+radius},grid,1);
    label(ctx,"N",x-10,79,20,16,16,WHITE,true);
    label(ctx,"S",x-10,356,20,17,16,WHITE,true);
    label(ctx,"W",51,y-9,20,18,16,WHITE,true);
    label(ctx,"E",358,y-9,20,18,16,WHITE,true);
    for(unsigned i=0;i<radar_view.count;i++) {
        const radar_tag_t *tag=&radar_tags[i];
        if(!tag->w) continue;
        const char *name=radar_view.targets[i].callsign;
        solid_rect(ctx,tag->x,tag->y,tag->w,tag->h,0x101A21);
        if(*name) label(ctx,name,tag->x+4,tag->y+1,tag->w-8,18,18,WHITE,false);
        if(tag->altitude[0]) label(ctx,tag->altitude,tag->x+4,tag->y+(*name?19:1),tag->w-8,17,16,track,false);
    }
    for(unsigned i=0;i<radar_view.count;i++) {
        const radar_target_t *target=&radar_view.targets[i];
        point_t point=radar_point(target);
        float tx=point.x,ty=point.y;
        float heading=target->bearing*(360.f/256)*DEG;
        float dx=sinf(heading),dy=-cosf(heading);
        point_t tip={tx+7*dx,ty+7*dy};
        point_t wings[3]={{tx-4*dx-4*dy,ty-4*dy+4*dx},tip,{tx-4*dx+4*dy,ty-4*dy-4*dx}};
        line(ctx,wings[0],wings[1],track,2);
        line(ctx,wings[1],wings[2],track,2);
    }
    line(ctx,(point_t){29,374},(point_t){401,374},grid,1);
    snprintf(text,sizeof(text),"%s  /  %u NM",radar_view.site[0]?radar_view.site:"LIVE RADAR",radar_view.range_nm);
    label(ctx,text,28,375,374,19,20,WHITE,true);
    if(radar_view.status==1) strcpy(text,"WAITING FOR ADS-B");
    else if(radar_view.status==3) strcpy(text,"ADS-B UNAVAILABLE");
    else if(!radar_view.count) strcpy(text,"NO AIRCRAFT IN RANGE");
    else if(radar_symbols_only) snprintf(text,sizeof(text),"LIVE / %u AC / %u SYMBOL ONLY",radar_view.count,radar_symbols_only);
    else snprintf(text,sizeof(text),"LIVE ADS-B / %u AIRCRAFT",radar_view.count);
    label(ctx,text,28,395,374,17,16,radar_view.status==2?track:WHITE,true);
}

static void draw_isis(lv_event_t *event)
{
    int64_t start=esp_timer_get_time();
    lv_draw_ctx_t *ctx=lv_event_get_draw_ctx(event);
    if(radar_view.status) {
        draw_live_radar(ctx);
        display_profile_add_draw(esp_timer_get_time()-start);
        return;
    }
    if(!mode_transition || !curtain_covers_clip(ctx->clip_area)) {
        bool show_attitude=data.connected && aircraft_layout!=AIRCRAFT_BALLOON;
        float bank=(float)data.bank_deg*DEG;
        float c=cosf(bank),s=sinf(bank),displacement=(float)data.pitch_deg*5.8f;
        draw_backdrop(ctx,show_attitude,c,s,displacement);
        if(aircraft_layout==AIRCRAFT_BALLOON) {
            balloon_hud(ctx);
        } else {
            attitude(ctx,c,s,displacement);tapes(ctx);fixed_scene(ctx);numbers(ctx);
            if(ctx->clip_area->y2>=sy(353)) hud(ctx);
            if(data.connected) {
                trend_arrow(ctx,34,rpm_layout() && speed_arrow<-52?-52:speed_arrow);
                trend_arrow(ctx,398,altitude_arrow);
            }
        }
        char g[16];
        if(data.connected && data.g_load_valid)
            snprintf(g,sizeof(g),"G %+.1f",lround(data.g_load*10)/10.0);
        else strcpy(g,"G --");
        detail_label(ctx,g,27,55);
    }
    if(mode_transition) draw_curtain(ctx);
    display_profile_add_draw(esp_timer_get_time()-start);
}

static void cover_isis(lv_event_t *event)
{
    // The custom backdrop paints the entire instrument, including its black margins.
    lv_cover_check_info_t *info=lv_event_get_param(event);
    info->res=LV_COVER_RES_COVER;
}
static void dirty(float x,float y,float w,float h)
{
    lv_area_t box={sx(x)-2,sy(y)-2,sx(x+w)+2,sy(y+h)+2};lv_obj_invalidate_area(instrument,&box);
}
static void invalidate_sweep(int previous,int current)
{
    if(previous==current) return;
    int lower=previous<current?previous:current;
    int upper=previous>current?previous:current;
    for(int y=0;y<480;y+=60) {
        int left=lower-y-61;
        int right=upper-y+2;
        if(left>479 || right<0) continue;
        lv_area_t strip={left<0?0:left,y,right>479?479:right,y+59};
        lv_obj_invalidate_area(instrument,&strip);
    }
}

static void mode_transition_step(void *target,int32_t phase)
{
    (void)target;
    int previous=sweep_edge(transition_phase);
    int current=sweep_edge(phase);
    if(transition_phase<CURTAIN_OPENING && phase>=CURTAIN_OPENING) {
        invalidate_sweep(previous,DIAGONAL_END);
        invalidate_sweep(0,current);
    } else {
        invalidate_sweep(previous,current);
    }
    if(curtain_title_visible(transition_phase)!=curtain_title_visible(phase))
        dirty(30,204,370,45);
    transition_phase=phase;
    if(phase>=CURTAIN_CLOSED) aircraft_layout=next_aircraft_layout;
}

static void mode_transition_done(lv_anim_t *animation)
{
    (void)animation;
    mode_transition=false;
    presented=data;
    lv_obj_invalidate(instrument);
}

static void update_prop_display(const flight_data_t *next)
{
    if(next->connected && next->aircraft_kind!=aircraft_layout && !mode_transition) {
        transition_phase=0;
        speed_arrow=altitude_arrow=0;
        speed_rate=0;
        speed_sample=next->ias_kt;
        speed_sample_tick=lv_tick_get();
        mode_transition=true;
        next_aircraft_layout=next->aircraft_kind;
        lv_anim_t animation;
        lv_anim_init(&animation);
        lv_anim_set_var(&animation,instrument);
        lv_anim_set_exec_cb(&animation,mode_transition_step);
        lv_anim_set_ready_cb(&animation,mode_transition_done);
        lv_anim_set_values(&animation,0,1000);
        lv_anim_set_time(&animation,2100);
        lv_anim_start(&animation);
    }
    if(mode_transition || !rpm_layout() || !next->connected) return;
    if(next->engine_count!=presented.engine_count) dirty(32,248,62,86);
    for(unsigned i=0;i<4;i++) {
        if(next->prop_rpm_valid[i]!=presented.prop_rpm_valid[i] ||
           next->prop_rpm_pct_valid[i]!=presented.prop_rpm_pct_valid[i] ||
           lround(next->prop_rpm[i])!=lround(presented.prop_rpm[i]) ||
           fabs(next->prop_rpm_pct[i]-presented.prop_rpm_pct[i])>=.5) {
            dirty(32,262+17*i,62,15);
            presented.prop_rpm_valid[i]=next->prop_rpm_valid[i];
            presented.prop_rpm_pct_valid[i]=next->prop_rpm_pct_valid[i];
            presented.prop_rpm[i]=next->prop_rpm[i];
            presented.prop_rpm_pct[i]=next->prop_rpm_pct[i];
        }
    }
}

static void refresh(lv_timer_t *timer)
{
    (void)timer;flight_data_t next;flight_data_snapshot(&next);
    smooth_pose(&next);
    radar_data_t radar;
    radar_snapshot(&radar);
    bool switched=(radar.status!=0)!=(radar_view.status!=0);
    bool targets_changed=memcmp(&radar,&radar_view,sizeof(radar))!=0;
    radar_view=radar;
    if(targets_changed && radar.status) layout_radar_tags();
    if(switched) {
        data=presented=next;
        lv_obj_invalidate(instrument);
        return;
    }
    if(radar.status) {
        if(targets_changed) dirty(26,78,378,345);
        if(next.connected!=presented.connected || lround(next.altitude_ft)!=lround(presented.altitude_ft) ||
           lround(next.ias_kt)!=lround(presented.ias_kt) || next.fuel_valid!=presented.fuel_valid ||
           lround(next.fuel_pct)!=lround(presented.fuel_pct) ||
           mach_display(&next)!=mach_display(&presented)) dirty(26,31,378,45);
        data=presented=next;
        return;
    }
    update_prop_display(&next);
    if(mode_transition) {data=next;return;}
    if(next.g_load_valid!=presented.g_load_valid ||
       lround(next.g_load*10)!=lround(presented.g_load*10)) {
        dirty(26,36,58,20);
        presented.g_load_valid=next.g_load_valid;
        presented.g_load=next.g_load;
    }
    if(aircraft_layout==AIRCRAFT_BALLOON) {
        if(next.connected!=data.connected) lv_obj_invalidate(instrument);
        else if(next.connected) {
            if(lround(next.altitude_ft)!=lround(presented.altitude_ft)) dirty(38,90,173,78);
            if(next.agl_valid!=presented.agl_valid || lround(next.agl_ft)!=lround(presented.agl_ft)) dirty(219,90,173,78);
            if(next.vertical_speed_valid!=presented.vertical_speed_valid ||
               lround(next.vertical_speed_fpm)!=lround(presented.vertical_speed_fpm)) dirty(38,178,354,78);
            if(next.ground_speed_valid!=presented.ground_speed_valid ||
               lround(next.ground_speed_kt)!=lround(presented.ground_speed_kt)) dirty(38,266,173,78);
            if(next.fuel_valid!=presented.fuel_valid || lround(next.fuel_pct)!=lround(presented.fuel_pct))
                dirty(219,266,173,78);
        }
        data=next;
        presented=next;
        return;
    }
    uint32_t elapsed=lv_tick_elaps(speed_sample_tick);
    if(!next.connected || !data.connected || next.age_ms>350 || next.seq<data.seq || elapsed>1000) {
        speed_sample=next.ias_kt;
        speed_sample_tick=lv_tick_get();
        speed_rate=0;
    } else if(next.seq!=data.seq && elapsed>=400) {
        double rate=(next.ias_kt-speed_sample)*1000/elapsed;
        speed_rate=fabs(rate)>20?0:(speed_rate+rate)*.5;
        speed_sample=next.ias_kt;
        speed_sample_tick=lv_tick_get();
    }
    int next_speed=next.connected && fabs(speed_rate)>=.35?trend_span(speed_rate*6*2.6):0;
    int next_altitude=next.connected && next.vertical_speed_valid && next.age_ms<=350?
                      trend_span(next.vertical_speed_fpm*.1*.168):0;
    if(next_speed!=speed_arrow) dirty(27,122,14,146);
    if(next_altitude!=altitude_arrow) dirty(391,122,14,146);
    speed_arrow=next_speed;
    altitude_arrow=next_altitude;
    if(next.connected && data.connected && next.seq>=data.seq) {
        bool speed_due=lv_tick_elaps(tape_sample_tick)>=100;
        if(speed_due) tape_sample_tick=lv_tick_get();
        else {
            next.ias_kt=data.ias_kt;
            next.heading_deg=data.heading_deg;
        }
        // Avoid forcing both wide tape regions into the same render deadline.
        if(!speed_due && lv_tick_elaps(altitude_sample_tick)>=100)
            altitude_sample_tick=lv_tick_get();
        else
            next.altitude_ft=data.altitude_ft;
    } else {
        tape_sample_tick=lv_tick_get();
        altitude_sample_tick=tape_sample_tick-66;
    }
    bool full=next.connected!=data.connected || next.demo!=data.demo;
    if(full) {data=next;presented=next;lv_obj_invalidate(instrument);return;}
    if(next.connected) {
        if(fabs(next.pitch_deg-presented.pitch_deg)>.05 ||
           fabs(remainder(next.bank_deg-presented.bank_deg,360))>.05) {
            dirty(90,55,233,257);presented.pitch_deg=next.pitch_deg;presented.bank_deg=next.bank_deg;
        }
        if(fabs(next.ias_kt-presented.ias_kt)>.05) {
            dirty(40,59,next.flap_limit_kt<=0 && next.vs0_kt>0?90:65,247);
            presented.ias_kt=next.ias_kt;
        }
        if(next.vs0_kt!=presented.vs0_kt || next.flap_limit_kt!=presented.flap_limit_kt ||
           next.overspeed_kt!=presented.overspeed_kt) {
            dirty(40,69,91,232);
            presented.vs0_kt=next.vs0_kt;
            presented.flap_limit_kt=next.flap_limit_kt;
            presented.overspeed_kt=next.overspeed_kt;
        }
        if(fabs(next.altitude_ft-presented.altitude_ft)>.5) {
            dirty(320,60,74,251);
            dirty(285,176,104,38);
            presented.altitude_ft=next.altitude_ft;
        }
        if(fabs(next.heading_deg-presented.heading_deg)>.05) {dirty(131,309,151,50);presented.heading_deg=next.heading_deg;}
        if(lround(next.qnh_hpa)!=lround(presented.qnh_hpa)) {dirty(310,306,82,34);presented.qnh_hpa=next.qnh_hpa;}
        if(mach_display(&next)!=mach_display(&presented)) dirty(MACH_X,MACH_Y,MACH_W,MACH_H);
        presented.mach=next.mach;presented.mach_valid=next.mach_valid;
        if(next.agl_valid!=presented.agl_valid || lround(next.agl_ft)!=lround(presented.agl_ft)) {
            dirty(286,35,120,22);
            presented.agl_valid=next.agl_valid;
            presented.agl_ft=next.agl_ft;
        }
        if(aircraft_layout==AIRCRAFT_PROP || rotor_layout()) {
            bool details_changed=next.detail_valid!=presented.detail_valid || next.rotor_count!=presented.rotor_count;
            for(unsigned i=0;i<DETAIL_COUNT;i++) {
                if(lround(next.details[i])!=lround(presented.details[i])) details_changed=true;
                presented.details[i]=next.details[i];
            }
            if(details_changed) {
                if(rotor_layout()) dirty(45,353,340,60);
                else dirty(84,35,222,21);
            }
            presented.detail_valid=next.detail_valid;
            presented.rotor_count=next.rotor_count;
        }
        if(aircraft_layout>=AIRCRAFT_HELI) {
            if(next.ground_speed_valid!=presented.ground_speed_valid || next.wind_valid!=presented.wind_valid ||
               next.collective_valid!=presented.collective_valid ||
               lround(next.ground_speed_kt)!=lround(presented.ground_speed_kt) ||
               lround(next.wind_kt)!=lround(presented.wind_kt) ||
               lround(next.collective_pct)!=lround(presented.collective_pct)) {
                dirty(45,390,340,23);
                if(rotor_layout()) dirty(39,312,94,25);
                presented.ground_speed_valid=next.ground_speed_valid;
                presented.wind_valid=next.wind_valid;
                presented.collective_valid=next.collective_valid;
                presented.ground_speed_kt=next.ground_speed_kt;
                presented.wind_kt=next.wind_kt;
                presented.collective_pct=next.collective_pct;
            }
            if(rotor_layout() && (next.rotor_valid!=presented.rotor_valid ||
                                  lround(next.rotor_pct)!=lround(presented.rotor_pct))) {
                dirty(45,375,340,23);
                presented.rotor_valid=next.rotor_valid;
                presented.rotor_pct=next.rotor_pct;
            }
        }
        if(next.tas_valid!=presented.tas_valid || lround(next.tas_kt)!=lround(presented.tas_kt)) {
            dirty(39,338,92,18);
            presented.tas_valid=next.tas_valid;
            presented.tas_kt=next.tas_kt;
        }
        char before[64],after[64];
        bool engines_dirty=next.engine_count!=presented.engine_count;
        for(unsigned i=0;i<4;i++) {
            engine_text(&presented,i,before,sizeof(before));engine_text(&next,i,after,sizeof(after));
            if(strcmp(before,after)) engines_dirty=true;
            presented.throttle_pct[i]=next.throttle_pct[i];presented.reverse_pct[i]=next.reverse_pct[i];presented.engine_valid[i]=next.engine_valid[i];
        }
        if(engines_dirty) dirty(45,354,340,36);
        presented.engine_count=next.engine_count;
        if(next.parking_brake_valid!=presented.parking_brake_valid ||
           next.parking_brake!=presented.parking_brake || next.wheel_brake!=presented.wheel_brake) {
            dirty(27,392,42,20);
            presented.parking_brake_valid=next.parking_brake_valid;
            presented.parking_brake=next.parking_brake;
            presented.wheel_brake=next.wheel_brake;
        }
        if(next.fuel_valid!=presented.fuel_valid || lround(next.fuel_pct)!=lround(presented.fuel_pct) ||
           next.flaps_index!=presented.flaps_index || next.flaps_positions!=presented.flaps_positions) {
            dirty(45,390,340,23);
            presented.fuel_pct=next.fuel_pct;
            presented.fuel_valid=next.fuel_valid;
            presented.flaps_index=next.flaps_index;
            presented.flaps_positions=next.flaps_positions;
        }
        if(next.vertical_speed_valid!=presented.vertical_speed_valid ||
           vertical_speed_display(&next)!=vertical_speed_display(&presented)) {
            dirty(293,335,100,20);
            presented.vertical_speed_fpm=next.vertical_speed_fpm;
            presented.vertical_speed_valid=next.vertical_speed_valid;
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
    lv_obj_add_event_cb(instrument,cover_isis,LV_EVENT_COVER_CHECK,NULL);
    lv_timer_t *timer=lv_timer_create(refresh,ISIS_REFRESH_MS,NULL);
    if(!timer) {lv_obj_del(instrument);instrument=NULL;return NULL;}
    refresh(timer);return instrument;
}
