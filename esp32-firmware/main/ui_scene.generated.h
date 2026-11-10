/* 由 scripts/generate-isis-ui.py 生成。所有数值均为 DEMO。 */
#pragma once
#include "lvgl.h"
enum { ISIS_ROLE_STATIC,ISIS_ROLE_AIRCRAFT_CENTER,ISIS_ROLE_ALTITUDE_TICK,ISIS_ROLE_ALTITUDE_VALUE,ISIS_ROLE_ATTITUDE,ISIS_ROLE_BANK,ISIS_ROLE_BANK_POINTER,ISIS_ROLE_DEMO_STATUS,ISIS_ROLE_GROUND,ISIS_ROLE_HEADING_TICK,ISIS_ROLE_HORIZON,ISIS_ROLE_IAS_TICK,ISIS_ROLE_MACH_DEMO,ISIS_ROLE_PITCH,ISIS_ROLE_QNH };
typedef struct { unsigned type, role; uint32_t color; unsigned width, count, font_size, center;
const lv_point_t *points; lv_area_t box; const char *text; } isis_command_t;
static const lv_point_t isis_points_0[] = {{86,50},{121,41},{175,36},{230,35},{285,36},{339,41},{374,50},{374,335},{339,344},{285,349},{230,350},{175,349},{121,344},{86,335}};
static const lv_point_t isis_points_1[] = {{86,227},{374,227},{374,335},{339,344},{285,349},{230,350},{175,349},{121,344},{86,335}};
static const lv_point_t isis_points_2[] = {{86,227},{374,227}};
static const lv_point_t isis_points_3[] = {{221,318},{239,318}};
static const lv_point_t isis_points_4[] = {{200,300},{260,300}};
static const lv_point_t isis_points_6[] = {{221,282},{239,282}};
static const lv_point_t isis_points_7[] = {{216,264},{244,264}};
static const lv_point_t isis_points_8[] = {{221,246},{239,246}};
static const lv_point_t isis_points_9[] = {{221,209},{239,209}};
static const lv_point_t isis_points_10[] = {{216,191},{244,191}};
static const lv_point_t isis_points_11[] = {{221,173},{239,173}};
static const lv_point_t isis_points_12[] = {{200,155},{260,155}};
static const lv_point_t isis_points_14[] = {{221,137},{239,137}};
static const lv_point_t isis_points_15[] = {{221,119},{239,119}};
static const lv_point_t isis_points_16[] = {{155,80},{162,76},{169,73},{176,70},{184,67},{191,65},{199,63},{207,62},{214,61}};
static const lv_point_t isis_points_17[] = {{246,61},{253,62},{261,63},{269,65},{276,67},{284,70},{291,73},{298,76},{305,80}};
static const lv_point_t isis_points_18[] = {{155,80},{146,64}};
static const lv_point_t isis_points_19[] = {{179,69},{174,56}};
static const lv_point_t isis_points_20[] = {{204,62},{202,49}};
static const lv_point_t isis_points_21[] = {{256,62},{258,49}};
static const lv_point_t isis_points_22[] = {{281,69},{286,56}};
static const lv_point_t isis_points_23[] = {{305,80},{314,64}};
static const lv_point_t isis_points_24[] = {{216,41},{244,41},{230,61}};
static const lv_point_t isis_points_25[] = {{230,66},{220,80},{240,80}};
static const lv_point_t isis_points_26[] = {{230,66},{220,80},{240,80},{230,66}};
static const lv_point_t isis_points_27[] = {{220,84},{240,84},{244,89},{216,89}};
static const lv_point_t isis_points_28[] = {{220,84},{240,84},{244,89},{216,89},{220,84}};
static const lv_point_t isis_points_29[] = {{78,305},{89,305}};
static const lv_point_t isis_points_30[] = {{74,272},{89,272}};
static const lv_point_t isis_points_32[] = {{78,240},{89,240}};
static const lv_point_t isis_points_33[] = {{74,208},{89,208}};
static const lv_point_t isis_points_35[] = {{78,175},{89,175}};
static const lv_point_t isis_points_36[] = {{74,142},{89,142}};
static const lv_point_t isis_points_38[] = {{78,110},{89,110}};
static const lv_point_t isis_points_39[] = {{74,78},{89,78}};
static const lv_point_t isis_points_41[] = {{85,208},{109,190},{109,224}};
static const lv_point_t isis_points_42[] = {{372,312},{395,312}};
static const lv_point_t isis_points_44[] = {{372,292},{385,292}};
static const lv_point_t isis_points_45[] = {{372,270},{385,270}};
static const lv_point_t isis_points_46[] = {{372,250},{385,250}};
static const lv_point_t isis_points_47[] = {{372,228},{385,228}};
static const lv_point_t isis_points_48[] = {{372,208},{395,208}};
static const lv_point_t isis_points_49[] = {{372,186},{385,186}};
static const lv_point_t isis_points_50[] = {{372,166},{385,166}};
static const lv_point_t isis_points_51[] = {{372,144},{385,144}};
static const lv_point_t isis_points_52[] = {{372,124},{385,124}};
static const lv_point_t isis_points_53[] = {{372,102},{395,102}};
static const lv_point_t isis_points_55[] = {{372,82},{385,82}};
static const lv_point_t isis_points_56[] = {{372,60},{385,60}};
static const lv_point_t isis_points_57[] = {{320,186},{454,186},{454,229},{320,229}};
static const lv_point_t isis_points_58[] = {{396,176},{454,176},{454,239},{396,239}};
static const lv_point_t isis_points_59[] = {{454,176},{396,176},{396,186},{320,186},{320,229},{396,229},{396,239},{454,239}};
static const lv_point_t isis_points_61[] = {{144,198},{190,198},{190,206},{144,206}};
static const lv_point_t isis_points_62[] = {{181,198},{190,198},{190,214},{181,214}};
static const lv_point_t isis_points_63[] = {{144,198},{190,198},{190,214},{181,214},{181,206},{144,206},{144,198}};
static const lv_point_t isis_points_64[] = {{222,198},{236,198},{236,212},{222,212}};
static const lv_point_t isis_points_65[] = {{222,198},{236,198},{236,212},{222,212},{222,198}};
static const lv_point_t isis_points_66[] = {{269,198},{315,198},{315,206},{269,206}};
static const lv_point_t isis_points_67[] = {{269,198},{278,198},{278,214},{269,214}};
static const lv_point_t isis_points_68[] = {{269,198},{315,198},{315,206},{278,206},{278,214},{269,214},{269,198}};
static const lv_point_t isis_points_70[] = {{138,375},{138,394},{321,394},{321,376}};
static const lv_point_t isis_points_71[] = {{150,394},{150,379}};
static const lv_point_t isis_points_72[] = {{168,394},{168,386}};
static const lv_point_t isis_points_73[] = {{186,394},{186,379}};
static const lv_point_t isis_points_75[] = {{204,394},{204,386}};
static const lv_point_t isis_points_76[] = {{222,394},{222,379}};
static const lv_point_t isis_points_77[] = {{241,394},{241,386}};
static const lv_point_t isis_points_78[] = {{259,394},{259,379}};
static const lv_point_t isis_points_80[] = {{277,394},{277,386}};
static const lv_point_t isis_points_81[] = {{295,394},{295,379}};
static const lv_point_t isis_points_82[] = {{313,394},{313,386}};
static const lv_point_t isis_points_83[] = {{222,370},{214,390},{231,390}};
static const lv_point_t isis_points_84[] = {{222,394},{214,408},{231,408}};
static const isis_command_t isis_commands[] = {
{.type=0,.color=0x2096d5,.role=ISIS_ROLE_ATTITUDE,.width=0,.count=14,.points=isis_points_0},
{.type=0,.color=0xb85948,.role=ISIS_ROLE_GROUND,.width=0,.count=9,.points=isis_points_1},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_HORIZON,.width=3,.count=2,.points=isis_points_2},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_PITCH,.width=3,.count=2,.points=isis_points_3},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_PITCH,.width=3,.count=2,.points=isis_points_4},
{.type=2,.color=0xd9dcda,.role=ISIS_ROLE_PITCH,.font_size=28,.center=0,.box={154,282,191,316},.text="10"},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_PITCH,.width=3,.count=2,.points=isis_points_6},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_PITCH,.width=3,.count=2,.points=isis_points_7},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_PITCH,.width=3,.count=2,.points=isis_points_8},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_PITCH,.width=3,.count=2,.points=isis_points_9},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_PITCH,.width=3,.count=2,.points=isis_points_10},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_PITCH,.width=3,.count=2,.points=isis_points_11},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_PITCH,.width=3,.count=2,.points=isis_points_12},
{.type=2,.color=0xd9dcda,.role=ISIS_ROLE_PITCH,.font_size=28,.center=0,.box={154,137,191,171},.text="10"},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_PITCH,.width=3,.count=2,.points=isis_points_14},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_PITCH,.width=3,.count=2,.points=isis_points_15},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_BANK,.width=3,.count=9,.points=isis_points_16},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_BANK,.width=3,.count=9,.points=isis_points_17},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_BANK,.width=3,.count=2,.points=isis_points_18},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_BANK,.width=3,.count=2,.points=isis_points_19},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_BANK,.width=3,.count=2,.points=isis_points_20},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_BANK,.width=3,.count=2,.points=isis_points_21},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_BANK,.width=3,.count=2,.points=isis_points_22},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_BANK,.width=3,.count=2,.points=isis_points_23},
{.type=0,.color=0xe6d34c,.role=ISIS_ROLE_STATIC,.width=0,.count=3,.points=isis_points_24},
{.type=0,.color=0x030609,.role=ISIS_ROLE_BANK_POINTER,.width=0,.count=3,.points=isis_points_25},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_BANK_POINTER,.width=3,.count=4,.points=isis_points_26},
{.type=0,.color=0x030609,.role=ISIS_ROLE_BANK_POINTER,.width=0,.count=4,.points=isis_points_27},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_BANK_POINTER,.width=1,.count=5,.points=isis_points_28},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_IAS_TICK,.width=3,.count=2,.points=isis_points_29},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_IAS_TICK,.width=4,.count=2,.points=isis_points_30},
{.type=2,.color=0xd9dcda,.role=ISIS_ROLE_IAS_TICK,.font_size=30,.center=0,.box={25,255,78,289},.text="250"},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_IAS_TICK,.width=3,.count=2,.points=isis_points_32},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_IAS_TICK,.width=4,.count=2,.points=isis_points_33},
{.type=2,.color=0xd9dcda,.role=ISIS_ROLE_IAS_TICK,.font_size=30,.center=0,.box={25,190,78,224},.text="270"},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_IAS_TICK,.width=3,.count=2,.points=isis_points_35},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_IAS_TICK,.width=4,.count=2,.points=isis_points_36},
{.type=2,.color=0xd9dcda,.role=ISIS_ROLE_IAS_TICK,.font_size=30,.center=0,.box={25,125,78,159},.text="290"},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_IAS_TICK,.width=3,.count=2,.points=isis_points_38},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_IAS_TICK,.width=4,.count=2,.points=isis_points_39},
{.type=2,.color=0xd9dcda,.role=ISIS_ROLE_IAS_TICK,.font_size=30,.center=0,.box={25,60,78,94},.text="310"},
{.type=0,.color=0xe6d34c,.role=ISIS_ROLE_STATIC,.width=0,.count=3,.points=isis_points_41},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_ALTITUDE_TICK,.width=4,.count=2,.points=isis_points_42},
{.type=2,.color=0xd9dcda,.role=ISIS_ROLE_ALTITUDE_TICK,.font_size=30,.center=0,.box={389,294,460,331},.text="375"},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_ALTITUDE_TICK,.width=3,.count=2,.points=isis_points_44},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_ALTITUDE_TICK,.width=3,.count=2,.points=isis_points_45},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_ALTITUDE_TICK,.width=3,.count=2,.points=isis_points_46},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_ALTITUDE_TICK,.width=3,.count=2,.points=isis_points_47},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_ALTITUDE_TICK,.width=4,.count=2,.points=isis_points_48},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_ALTITUDE_TICK,.width=3,.count=2,.points=isis_points_49},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_ALTITUDE_TICK,.width=3,.count=2,.points=isis_points_50},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_ALTITUDE_TICK,.width=3,.count=2,.points=isis_points_51},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_ALTITUDE_TICK,.width=3,.count=2,.points=isis_points_52},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_ALTITUDE_TICK,.width=4,.count=2,.points=isis_points_53},
{.type=2,.color=0xd9dcda,.role=ISIS_ROLE_ALTITUDE_TICK,.font_size=30,.center=0,.box={389,84,460,121},.text="385"},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_ALTITUDE_TICK,.width=3,.count=2,.points=isis_points_55},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_ALTITUDE_TICK,.width=3,.count=2,.points=isis_points_56},
{.type=0,.color=0x030609,.role=ISIS_ROLE_STATIC,.width=0,.count=4,.points=isis_points_57},
{.type=0,.color=0x030609,.role=ISIS_ROLE_STATIC,.width=0,.count=4,.points=isis_points_58},
{.type=1,.color=0xe6d34c,.role=ISIS_ROLE_STATIC,.width=3,.count=8,.points=isis_points_59},
{.type=2,.color=0x2cf525,.role=ISIS_ROLE_ALTITUDE_VALUE,.font_size=36,.center=0,.box={330,189,451,230},.text="38000"},
{.type=0,.color=0x030609,.role=ISIS_ROLE_STATIC,.width=0,.count=4,.points=isis_points_61},
{.type=0,.color=0x030609,.role=ISIS_ROLE_STATIC,.width=0,.count=4,.points=isis_points_62},
{.type=1,.color=0xe6d34c,.role=ISIS_ROLE_STATIC,.width=3,.count=7,.points=isis_points_63},
{.type=0,.color=0x030609,.role=ISIS_ROLE_AIRCRAFT_CENTER,.width=0,.count=4,.points=isis_points_64},
{.type=1,.color=0xe6d34c,.role=ISIS_ROLE_AIRCRAFT_CENTER,.width=3,.count=5,.points=isis_points_65},
{.type=0,.color=0x030609,.role=ISIS_ROLE_STATIC,.width=0,.count=4,.points=isis_points_66},
{.type=0,.color=0x030609,.role=ISIS_ROLE_STATIC,.width=0,.count=4,.points=isis_points_67},
{.type=1,.color=0xe6d34c,.role=ISIS_ROLE_STATIC,.width=3,.count=7,.points=isis_points_68},
{.type=2,.color=0x2cf525,.role=ISIS_ROLE_MACH_DEMO,.font_size=30,.center=0,.box={45,355,103,390},.text=".85"},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_STATIC,.width=3,.count=4,.points=isis_points_70},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_HEADING_TICK,.width=3,.count=2,.points=isis_points_71},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_HEADING_TICK,.width=3,.count=2,.points=isis_points_72},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_HEADING_TICK,.width=3,.count=2,.points=isis_points_73},
{.type=2,.color=0xd9dcda,.role=ISIS_ROLE_HEADING_TICK,.font_size=28,.center=1,.box={161,354,210,385},.text="22"},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_HEADING_TICK,.width=3,.count=2,.points=isis_points_75},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_HEADING_TICK,.width=3,.count=2,.points=isis_points_76},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_HEADING_TICK,.width=3,.count=2,.points=isis_points_77},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_HEADING_TICK,.width=3,.count=2,.points=isis_points_78},
{.type=2,.color=0xd9dcda,.role=ISIS_ROLE_HEADING_TICK,.font_size=28,.center=1,.box={234,354,283,385},.text="24"},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_HEADING_TICK,.width=3,.count=2,.points=isis_points_80},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_HEADING_TICK,.width=3,.count=2,.points=isis_points_81},
{.type=1,.color=0xd9dcda,.role=ISIS_ROLE_HEADING_TICK,.width=3,.count=2,.points=isis_points_82},
{.type=0,.color=0x2cf525,.role=ISIS_ROLE_STATIC,.width=0,.count=3,.points=isis_points_83},
{.type=0,.color=0xe6d34c,.role=ISIS_ROLE_STATIC,.width=0,.count=3,.points=isis_points_84},
{.type=2,.color=0x4dc9e5,.role=ISIS_ROLE_QNH,.font_size=34,.center=0,.box={360,348,459,387},.text="1013"},
{.type=2,.color=0xe6d34c,.role=ISIS_ROLE_DEMO_STATUS,.font_size=16,.center=0,.box={30,19,114,43},.text="DEMO"},
};
#define ISIS_BACKGROUND 0x030609
#define ISIS_COMMAND_COUNT (sizeof(isis_commands)/sizeof(isis_commands[0]))
