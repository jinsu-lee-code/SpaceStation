using System.Collections.Generic;
using System.IO;
using System.Linq;
using SpaceStation.Audio;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 5-8 소리 일괄 설정 (메뉴 SpaceStation/Audio/Setup). 여러 번 실행해도 결과가 같다.
    /// - 가져오기 설정: UI·건설은 PCM(지연 최소), 이벤트는 Vorbis, 반복음은 압축 상태 메모리, 음악은 스트리밍
    /// - SoundLibrary 에셋의 클립을 파일 이름으로 채움 (볼륨 등 조정값은 유지)
    /// - 씬: Audio 오브젝트(AudioService + GameAudio) 생성·연결, HUD 버튼과 버튼 프리팹에 UiSound 추가
    /// 소리 파일 가공·합성은 저장소의 AudioOriginals/build_audio.py (원본은 같은 폴더에 보관).
    /// </summary>
    public static class AudioSetup
    {
        private const string AudioRoot = "Assets/_Project/Audio";
        private const string LibraryPath = "Assets/_Project/Data/Audio/SoundLibrary.asset";
        private const string ButtonPrefab = "Assets/_Project/Prefabs/UI/PF_BuildButton.prefab";
        private const string TabPrefab = "Assets/_Project/Prefabs/UI/PF_BuildTab.prefab";

        /// <summary>SoundLibrary 필드 → 파일 이름 (끝이 _01 등인 변형은 접두어로 모두 포함).</summary>
        private static readonly Dictionary<string, string[]> Map = new Dictionary<string, string[]>
        {
            { "UiClick", new[] { "ui_click_" } },
            { "UiHover", new[] { "ui_hover_" } },
            { "UiTab", new[] { "ui_tab" } },
            { "UiOpen", new[] { "ui_open" } },
            { "UiClose", new[] { "ui_close" } },
            { "UiError", new[] { "ui_error" } },
            { "UiSpeedUp", new[] { "ui_speed_up" } },
            { "UiPause", new[] { "ui_pause" } },
            { "UiResume", new[] { "ui_resume" } },
            // UI 한 벌 홀로그램 (2026-11-25, build_audio2.py — D-6 동작 9)
            { "UiCancel", new[] { "ui_cancel" } },
            { "UiToggleOn", new[] { "ui_toggle_on" } },
            { "UiToggleOff", new[] { "ui_toggle_off" } },
            { "UiSlider", new[] { "ui_slider" } },
            { "BuildSelect", new[] { "build_select" } },
            { "BuildRotate", new[] { "build_rotate_" } },
            // 배치 · 철거 크기 3갈래 (2026-11-25 사운드 개편 1-1 · D-6, build_audio2.py — 1칸 _s · 여러 칸 _m · 진화 _evo)
            { "BuildPlace", new[] { "build_place_s" } },
            { "BuildPlaceM", new[] { "build_place_m" } },
            { "BuildPlaceEvo", new[] { "build_place_evo" } },
            { "BuildRemove", new[] { "build_remove_s" } },
            { "BuildRemoveM", new[] { "build_remove_m" } },
            { "BuildRemoveEvo", new[] { "build_remove_evo" } },
            { "BuildConnect", new[] { "build_connect" } },
            { "RepairStart", new[] { "repair_start" } },
            { "RepairDone", new[] { "repair_done" } },
            { "Maintain", new[] { "maintain" } },
            { "ModuleMovePick", new[] { "module_move_pick" } },   // 2026-11-26 바깥 물건 (build_audio2.py "바깥 물건" 구역)
            { "ModuleMoveDrop", new[] { "module_move_drop" } },
            { "EventPositive", new[] { "event_positive" } },
            // 운석 · 방어 · 파손 (2026-11-25 사운드 개편 1-1, build_audio2.py "운석 · 방어 · 파손" 구역): 변형은 이름을 다 적음 (B 묶음 shield_deflect_hex · turret_laser_1 등이 앞부분으로 섞이지 않게)
            { "MeteorIncoming", new[] { "meteor_incoming", "meteor_incoming_02" } },
            { "MeteorImpact", new[] { "meteor_impact" } },
            { "MeteorImpactHullL", new[] { "meteor_impact_hull_l_" } },
            { "MeteorImpactHullS", new[] { "meteor_impact_hull_s_" } },
            { "MeteorImpactArmorL", new[] { "meteor_impact_armor_l_" } },
            { "MeteorImpactArmorS", new[] { "meteor_impact_armor_s_" } },
            { "MeteorImpactShieldL", new[] { "meteor_impact_shield_l_" } },
            { "MeteorImpactShieldS", new[] { "meteor_impact_shield_s_" } },
            { "MeteorExplode", new[] { "meteor_explode", "meteor_explode_02", "meteor_explode_03" } },
            { "TurretLaser", new[] { "turret_laser", "turret_laser_02", "turret_laser_03" } },
            { "ShieldDeflect", new[] { "shield_deflect", "shield_deflect_02", "shield_deflect_03" } },
            // 습격 포격 · 방어 단계 · 바깥 진화 연출 · 오가는 것 (2026-11-26 작업자 P4, build_audio2.py 같은 이름 구역) — 바깥 연출 칸 일곱은 1인칭 체험 · 소리 풍경과 같은 파일 (한 물건 = 소리 하나)
            { "RaidIncoming", new[] { "raid_incoming", "raid_incoming_02" } },
            { "RaidImpact", new[] { "raid_impact_" } },
            { "ShieldDeflectHex", new[] { "shield_deflect_hex", "shield_deflect_hex_02", "shield_deflect_hex_03" } },
            { "ShieldDeflectAurora", new[] { "shield_deflect_aurora", "shield_deflect_aurora_02", "shield_deflect_aurora_03" } },
            { "ShieldDeflectCrystal", new[] { "shield_deflect_crystal", "shield_deflect_crystal_02", "shield_deflect_crystal_03" } },
            { "TurretLaser1", new[] { "turret_laser_1", "turret_laser_1_02", "turret_laser_1_03" } },
            { "PrismBeam", new[] { "prism_beam", "prism_beam_02", "prism_beam_03" } },
            { "FxAuroraLoop", new[] { "fx_aurora_loop" } },
            { "FxLightBeamLoop", new[] { "fx_lightbeam_loop" } },
            { "FxLightning", new[] { "fx_lightning_" } },
            { "FxFireworkLaunch", new[] { "firework_launch_" } },
            { "FxFireworkBurst", new[] { "firework_burst_" } },
            { "FxDroneShowLoop", new[] { "droneshow_swarm_loop" } },
            { "FxFlumeLoop", new[] { "scape_bio_sluice_loop" } },
            { "FxFlumeSplash", new[] { "flume_big_splash" } },
            { "FxBoatLoop", new[] { "scape_port_wake_loop" } },
            { "FxPatrolHoverLoop", new[] { "patrol_hover_loop" } },
            { "FxSiren", new[] { "patrol_siren" } },
            { "FxRainLoop", new[] { "scape_bio_rain_loop" } },
            { "ExtShipEngine", new[] { "ext_ship_engine_loop" } },
            { "ExtMeteorPass", new[] { "ext_meteor_pass", "ext_meteor_pass_02" } },
            { "ModuleDamaged", new[] { "module_damaged" } },
            { "ModuleDestroyed", new[] { "module_destroyed" } },
            { "StormSpark", new[] { "storm_spark_" } },
            { "OxygenLeak", new[] { "oxygen_leak" } },
            { "SupplyArrive", new[] { "supply_arrive" } },
            { "GradeUp", new[] { "grade_up" } },
            { "Victory", new[] { "victory" } },
            { "GameOver", new[] { "game_over" } },
            // 경보 위계 4단계 (2026-11-25, Audio/Event — SOUND_LIST_NEW D-3, build_audio2.py "경보 위계 4단계" 구역 · AlertSound)
            { "AlertInfo", new[] { "alert_info" } },
            { "AlertCaution", new[] { "alert_caution" } },
            { "AlertDanger", new[] { "alert_danger" } },
            { "AlertCritical", new[] { "alert_critical" } },
            // 바깥 알림 (2026-11-26, Audio/Event — SOUND_LIST_NEW 2번, build_audio2.py "바깥 알림" 구역 · NoticeSound)
            { "ResearchDone", new[] { "research_done" } },
            { "UnlockReveal", new[] { "unlock_reveal" } },
            { "PolicyEnact", new[] { "policy_enact" } },
            { "SocialCrisis", new[] { "social_crisis" } },
            { "PlagueOutbreak", new[] { "plague_outbreak" } },
            { "PlagueCured", new[] { "plague_cured" } },
            { "ContactArrive1", new[] { "contact_arrive_1" } },
            { "ContactArrive2", new[] { "contact_arrive_2" } },
            { "ContactArrive3", new[] { "contact_arrive_3" } },
            { "ContactResult", new[] { "contact_result" } },
            { "MeteorShowerWarn", new[] { "meteor_shower_warn" } },
            { "HeatWarning", new[] { "heat_warning" } },
            { "Dawn", new[] { "dawn" } },
            { "Dusk", new[] { "dusk" } },
            { "ModuleDisconnect", new[] { "module_disconnect" } },
            { "ModuleReconnect", new[] { "module_reconnect" } },
            { "AlarmLoop", new[] { "alarm_loop" } },
            { "StormLoop", new[] { "solar_storm_loop" } },
            { "StationHum", new[] { "amb_station_loop" } },
            { "ExtHum1", new[] { "ext_hum_1_loop" } },
            { "ExtHum2", new[] { "ext_hum_2_loop" } },
            { "ExtHum3", new[] { "ext_hum_3_loop" } },
            { "Footstep", new[] { "step_metal_01", "step_metal_02", "step_metal_03", "step_metal_04", "step_metal_05", "step_metal_06", "step_metal_07", "step_metal_08", "step_metal_09" } },
            // 바닥별 발소리 (2026-11-25, Audio/Interior — SOUND_LIST_NEW 4-1, build_audio2.py "바닥별 발소리" 구역). 금속 판 걷기는 step_metal_01~09 그대로
            { "FootstepRun", new[] { "step_metal_run_" } },
            { "StepGrate", new[] { "step_grate_walk_" } },
            { "StepGrateRun", new[] { "step_grate_run_" } },
            { "StepGlass", new[] { "step_glass_walk_" } },
            { "StepGlassRun", new[] { "step_glass_run_" } },
            { "StepGrass", new[] { "step_grass_walk_" } },
            { "StepGrassRun", new[] { "step_grass_run_" } },
            { "StepWood", new[] { "step_wood_walk_" } },
            { "StepWoodRun", new[] { "step_wood_run_" } },
            { "StepStone", new[] { "step_stone_walk_" } },
            { "StepStoneRun", new[] { "step_stone_run_" } },
            { "StepWater", new[] { "step_water_walk_" } },
            { "StepWaterRun", new[] { "step_water_run_" } },
            { "RoomLife", new[] { "amb_room_life_loop" } },
            { "RoomWater", new[] { "amb_room_water_loop" } },
            { "RoomMachine", new[] { "amb_room_machine_loop" } },
            { "RoomHeat", new[] { "amb_room_heat_loop" } },
            { "RoomElectric", new[] { "amb_room_electric_loop" } },
            { "RoomHall", new[] { "amb_room_hall_loop" } },
            { "InteriorAlarm", new[] { "interior_alarm_loop" } },
            // 방 환경음 1단계 · 상태 층 (2026-11-25, Audio/Interior · Interior/State — SOUND_LIST_NEW 4-4, build_audio2.py "실제 녹음 ⑥" 구역). 가끔 소리 꾸러미는 SS_Spot_*.asset (RoomSpots)
            { "Room1Life", new[] { "amb_life_1_loop" } },
            { "Room1Water", new[] { "amb_water_1_loop" } },
            { "Room1Machine", new[] { "amb_machine_1_loop" } },
            { "Room1Heat", new[] { "amb_heat_1_loop" } },
            { "Room1Electric", new[] { "amb_electric_1_loop" } },
            { "Room1Hall", new[] { "amb_hall_1_loop" } },
            { "StateSparkLoop", new[] { "state_spark_loop" } },
            { "StateCreak", new[] { "state_creak_" } },
            { "StatePowerDown", new[] { "state_powerdown" } },
            { "StateFanStop", new[] { "state_fan_stop" } },
            { "StateBoilLoop", new[] { "state_boil_loop" } },
            // 사운드 개편 (2026-11-20, AudioOriginals/build_audio2.py)
            { "DoorOpen", new[] { "door_open_" } },
            { "DoorClose", new[] { "door_close_" } },
            { "Door1Open", new[] { "door1_open_" } },
            { "Door1Close", new[] { "door1_close_" } },
            { "AirlockOpen", new[] { "airlock_open_" } },
            { "AirlockClose", new[] { "airlock_close_" } },
            // 1인칭 할 일 (2026-11-25, Audio/Task · Audio/UI — SOUND_LIST_NEW 4-3, build_audio2.py "1인칭 할 일" 구역). 큰 크기 (pipe_burst_big_loop · fire_big_loop)는 파일만 (예정)
            { "HoldLoop", new[] { "hold_loop" } },
            { "HoldDone", new[] { "hold_done" } },
            { "FieldRepairLoop", new[] { "fieldrepair_loop" } },
            { "PipeBurstLoop", new[] { "pipe_burst_loop" } },
            { "ValveTurn", new[] { "valve_turn" } },
            { "BreakerDoor", new[] { "breaker_door" } },
            { "BreakerSwitch", new[] { "breaker_switch" } },
            { "BreakerFlip", new[] { "breaker_flip" } },
            { "FireLoop", new[] { "fire_loop" } },
            { "ExtinguisherPickup", new[] { "extinguisher_pickup" } },
            { "Extinguish", new[] { "extinguish" } },
            { "FilterSwap", new[] { "filter_swap" } },
            // 합성 · 이민선 · 이동 장치 · 엘리베이터 · 방 드나들기 (2026-11-25, Audio/Event · Audio/Interior — SOUND_LIST_NEW 2번 · 4-1, build_audio2.py 같은 이름 구역)
            { "SynthStart", new[] { "synth_start" } },
            { "SynthLoop", new[] { "synth_loop" } },
            { "SynthDone1", new[] { "synth_done_1" } },
            { "SynthDone2", new[] { "synth_done_2" } },
            { "SynthDone3", new[] { "synth_done_3" } },
            { "ShipImmigrantArrive", new[] { "ship_immigrant_arrive" } },
            { "ShipCargoDock", new[] { "ship_cargo_dock" } },   // 2026-11-26 바깥 물건
            { "CrateAppear", new[] { "crate_appear_" } },
            { "TransitOpen", new[] { "transit_open" } },
            { "TransitRide0", new[] { "transit_ride_0" } },
            { "TransitRide1", new[] { "transit_ride_1" } },
            { "TransitRide2", new[] { "transit_ride_2" } },
            { "TransitRide3", new[] { "transit_ride_3" } },
            { "TransitRide4", new[] { "transit_ride_4" } },
            { "TransitArrive", new[] { "transit_arrive" } },
            { "LiftButton1", new[] { "lift_button_1" } },
            { "LiftButton2", new[] { "lift_button_2" } },
            { "LiftMove1Loop", new[] { "lift_move_1_loop" } },
            { "LiftMove2Loop", new[] { "lift_move_2_loop" } },
            { "LiftArrive1", new[] { "lift_arrive_1" } },
            { "LiftArrive2", new[] { "lift_arrive_2" } },
            { "InteriorEnter", new[] { "interior_enter" } },
            { "InteriorExit", new[] { "interior_exit" } },
            { "PadTravel", new[] { "pad_travel" } },
            // 내부 장치 (2026-11-26, Audio/Interior · Audio/Task — SOUND_LIST_NEW 4-1 · 4-2 · 4-3, build_audio2.py "내부 장치" 구역)
            { "DoorVaultOpen", new[] { "door_vault_open" } },
            { "DoorVaultClose", new[] { "door_vault_close" } },
            { "LightBridgeLoop", new[] { "lightbridge_loop" } },
            { "StepSuit", new[] { "step_suit_" } },
            { "SuitHelmet", new[] { "suit_helmet" } },
            { "SuitBreathLoop", new[] { "suit_breath_loop" } },
            { "PadRaise", new[] { "pad_raise" } },
            { "PadLower", new[] { "pad_lower" } },
            { "PadHoloLoop", new[] { "pad_holo_loop" } },
            { "RadarPingLoop", new[] { "radar_ping_loop" } },
            { "SignalNoiseLoop", new[] { "signal_noise_loop" } },
            { "SignalLock", new[] { "signal_lock" } },
            { "RequestPop", new[] { "request_pop" } },
            { "RequestDone", new[] { "request_done" } },
            { "WaterPour", new[] { "water_pour" } },
            { "VentFix", new[] { "vent_fix" } },
            { "VentRattle", new[] { "vent_rattle_" } },
            { "CratePickup", new[] { "crate_pickup" } },
            { "DroneBreach", new[] { "drone_breach" } },
            { "DroneHoverLoop", new[] { "drone_hover_loop" } },
            { "DroneDown", new[] { "drone_down" } },
            // 방 장치 소리 · 바깥 일이 안에서 (2026-11-26, Audio/Interior/Props · Outside — SOUND_LIST_NEW 4-4 · D-8 · D-10). 번호 부름 = 1단계 생활 가끔 소리 띵동 그대로
            { "ObjConveyorLoop", new[] { "obj_conveyor_loop" } },
            { "ObjConveyorClunk", new[] { "obj_conveyor_clunk" } },
            { "ObjServerFanLoop", new[] { "obj_server_fan_loop" } },
            { "ObjScreenHumLoop", new[] { "obj_screen_hum_loop" } },
            { "ObjIceClatter", new[] { "obj_ice_clatter_" } },
            { "ObjPlantRustle", new[] { "obj_plant_rustle_" } },
            { "ObjQueueChime", new[] { "spot_life_1_03" } },
            { "HullThud", new[] { "hull_thud_" } },
            { "HullRumble", new[] { "hull_rumble" } },
            { "LightFlicker", new[] { "light_flicker_" } },
            { "RadioStatic", new[] { "radio_static" } },
            { "DockClank", new[] { "dock_clank" } },
            // 체험 · 미니게임 공용 (2026-11-23, Audio/Ride — SOUND_LIST_NEW 5-1)
            { "GameStart", new[] { "game_start" } },
            { "GameTick", new[] { "game_tick" } },
            { "GameScore", new[] { "game_score" } },
            { "GameMiss", new[] { "game_miss" } },
            { "GameClear", new[] { "game_clear" } },
            { "GameFail", new[] { "game_fail" } },
            { "GameRecord", new[] { "game_record" } },
            { "RideBoard", new[] { "ride_board" } },
            { "RideLeave", new[] { "ride_leave" } },
            // 체험 · 미니게임 대표 소리 (2026-11-23, Audio/Ride — SOUND_LIST_NEW 5-2): 궤도 도시 · 바이오스피어 · 궤도 방어망 · 수리 사령부
            { "OrbitTrainLoop", new[] { "otrain_loop" } },
            { "OrbitTrainBrake", new[] { "otrain_brake" } },
            { "OrbitTrainStopPerfect", new[] { "otrain_stop_perfect" } },
            { "BioSluiceWheel", new[] { "bio_sluice_wheel" } },
            { "BioWaterFlow", new[] { "bio_water_flow" } },
            { "BioFireflyCatch", new[] { "bio_firefly_catch" } },
            { "OdefLeverPull", new[] { "odef_lever_pull" } },
            { "OdefShieldUnfurl", new[] { "odef_shield_unfurl" } },
            { "OdefStarHitShield", new[] { "odef_star_hit_shield" } },
            { "OdefCrankLoop", new[] { "odef_crank_loop" } },
            { "OdefBeamReact", new[] { "odef_beam_react" } },
            { "OdefCannonFire", new[] { "odef_cannon_fire" } },
            { "OdefStarBurst", new[] { "odef_star_burst" } },
            { "RcmdRopeLoop", new[] { "rcmd_rope_loop" } },
            { "RcmdBasketCreak", new[] { "rcmd_basket_creak" } },
            { "RcmdBellToll", new[] { "scape_repair_bell_01" } },
            { "RcmdLanternShimmer", new[] { "rcmd_lantern_shimmer" } },
            { "RcmdDronesRise", new[] { "rcmd_drones_rise" } },
            { "RcmdShowFinale", new[] { "rcmd_show_finale" } },
            { "RcmdLanternNote", new[] { "rcmd_lantern_note" } },
            // 5-2 둘째 묶음 (2026-11-23): 궤도 항만 · AI 중추 · 항성 발전망 · 심우주 통신기지 (소리 풍경 소리를 다시 쓰는 칸은 scape_ 이름)
            { "OportPaddleLoop", new[] { "oport_paddle_loop" } },
            { "OportBoatDock", new[] { "oport_boat_dock" } },
            { "OportClamOpen", new[] { "oport_clam_open" } },
            { "OportClamSnap", new[] { "oport_clam_snap" } },
            { "OportPearlGet", new[] { "oport_pearl_get" } },
            { "OportThrow", new[] { "oport_throw" } },
            { "OportPlop", new[] { "scape_port_fish_02" } },
            { "AicoreBookOpen", new[] { "aicore_book_open" } },
            { "AicoreCrystalSwell", new[] { "aicore_crystal_swell" } },
            { "AicoreDroneRideLoop", new[] { "aicore_droneride_loop" } },
            { "AicoreDroneLand", new[] { "aicore_drone_land" } },
            { "AicoreOrbCollect", new[] { "aicore_orb_collect" } },
            { "AicoreSynapseNote", new[] { "aicore_synapse_note" } },
            { "SgridSwingWhoosh", new[] { "sgrid_swing_whoosh" } },
            { "SgridFlowerBell", new[] { "scape_stellar_flowerbell_01" } },
            { "SgridBeeGuardLoop", new[] { "sgrid_bee_guard_loop" } },
            { "SgridHoneySlurp", new[] { "sgrid_honey_slurp" } },
            { "SgridCaught", new[] { "sgrid_caught" } },
            { "DscDishMotor", new[] { "dsc_dish_motor" } },
            { "DscSignalLock", new[] { "scape_deep_signal_01" } },
            { "DscSignalMotif", new[] { "scape_deep_motif" } },
            { "DscWishBegin", new[] { "scape_stellar_flowerbell_01" } },
            { "DscWishStar", new[] { "scape_defense_star_01" } },
            { "DscWishSparkle", new[] { "dsc_wish_sparkle" } },
            { "DscStaticLoop", new[] { "dsc_static_loop" } },
            { "DscTuneLock", new[] { "dsc_tune_lock" } },
            { "DscStarCatch", new[] { "dsc_star_catch" } },
            { "TorusWhistle", new[] { "torus_whistle" } },
            { "TorusRunLoop", new[] { "torus_run_loop" } },
            { "TorusBrake", new[] { "torus_brake" } },
            { "FerrisDoor", new[] { "ferris_door" } },
            { "FerrisCreakLoop", new[] { "ferris_creak_loop" } },
            { "CarouselBell", new[] { "carousel_bell" } },
            { "CarouselMusicLoop", new[] { "carousel_music_loop" } },
            { "FlumeChainLoop", new[] { "flume_chain_loop" } },
            { "FlumeRushLoop", new[] { "scape_bio_sluice_loop" } },
            { "FlumeBigSplash", new[] { "flume_big_splash" } },
            { "FlumeDock", new[] { "flume_dock" } },
            { "MazeHumLoop", new[] { "scape_defense_battery_loop" } },
            { "MazeExit", new[] { "maze_exit" } },
            { "DroneShowSwarmLoop", new[] { "droneshow_swarm_loop" } },
            { "DroneShowRise", new[] { "rcmd_drones_rise" } },
            { "FireworkLaunch", new[] { "firework_launch_" } },
            { "FireworkBurst", new[] { "firework_burst_" } },
            { "FireworkLever", new[] { "firework_lever" } },
            { "LightningZap", new[] { "lightning_zap" } },
            { "LightningHumLoop", new[] { "lightning_hum_loop" } },
            { "PatrolHoverLoop", new[] { "patrol_hover_loop" } },
            { "PatrolSiren", new[] { "patrol_siren" } },
            { "DomeDim", new[] { "dome_dim" } },
            { "DomeBrighten", new[] { "dome_brighten" } },
            { "DomeShowLoop", new[] { "dome_show_loop" } },
            { "RefineryFurnaceLoop", new[] { "refinery_furnace_loop" } },
            { "RefineryLadlePour", new[] { "refinery_ladle_pour" } },
            { "RefineryCraneMoveLoop", new[] { "refinery_crane_move_loop" } },
            { "RefineryCraneGrab", new[] { "refinery_crane_grab" } },
            // 5-2 넷째 묶음 (2026-11-23): 1단계 넷 (망원경 · 포탑 · 용접 · 조선소) + 남은 빌린 소리 정리
            { "T1TelescopeIn", new[] { "t1_telescope_in" } },
            { "T1TelescopeOut", new[] { "t1_telescope_out" } },
            { "T1TelescopeZoom", new[] { "t1_telescope_zoom" } },
            { "T1TurretServoLoop", new[] { "t1_turret_servo_loop" } },
            { "T1WeldLoop", new[] { "t1_weld_loop" } },
            { "ShipyardGantryLoop", new[] { "refinery_crane_move_loop" } },
            { "OdefStarIncoming", new[] { "odef_star_incoming" } },
            { "RcmdBasketCall", new[] { "rcmd_basket_call" } },
            { "AicoreCrystalFade", new[] { "aicore_crystal_fade" } },
            { "BioFieldBloom", new[] { "bio_field_bloom" } },
            { "MusicDay", new[] { "amb_in_game_loop", "amb_space_loop" } },
            { "MusicNight", new[] { "amb_night_loop" } },
            { "MusicCrisis", new[] { "amb_crisis_loop" } },
            { "MusicMenu", new[] { "amb_mainmenu_loop" } },
            // 내부 음악 4곡 (2026-11-23, AudioOriginals/build_music.py)
            { "MusicInterior0", new[] { "music_interior_0" } },
            { "MusicInterior1", new[] { "music_interior_1" } },
            { "MusicInterior2", new[] { "music_interior_2" } },
            { "MusicInterior3", new[] { "music_interior_3" } },
        };

        [MenuItem("SpaceStation/Audio/Setup")]
        public static void Run()
        {
            var clips = ImportSettings();
            var library = BuildLibrary(clips);
            WireScene(library);
            AddButtonSounds();
            Debug.Log($"[AudioSetup] 클립 {clips.Count}개, 라이브러리 {LibraryPath}, 씬 연결 완료");
        }

        // ---------------- 가져오기 설정 ----------------

        private static Dictionary<string, AudioClip> ImportSettings()
        {
            var result = new Dictionary<string, AudioClip>();
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { AudioRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = Path.GetFileNameWithoutExtension(path);
                var importer = (AudioImporter)AssetImporter.GetAtPath(path);
                // 내부 음악 층은 여러 곡을 같은 dsp 순간에 함께 시작해야 해서 스트리밍 대신 압축 상태 메모리 (2026-11-23)
                bool layer = name.StartsWith("music_interior_");
                bool music = (path.EndsWith(".mp3") || path.EndsWith(".ogg")) && !layer;
                bool loop = name.EndsWith("_loop") || layer;
                bool snappy = path.Contains("/UI/") || path.Contains("/Build/");

                var s = importer.defaultSampleSettings;
                if (music)
                {
                    s.loadType = AudioClipLoadType.Streaming;
                    s.compressionFormat = AudioCompressionFormat.Vorbis;
                    s.quality = 0.6f;
                    s.preloadAudioData = false;
                }
                else if (loop)
                {
                    s.loadType = AudioClipLoadType.CompressedInMemory;
                    s.compressionFormat = AudioCompressionFormat.Vorbis;
                    s.quality = 0.75f;
                    s.preloadAudioData = true;
                }
                else
                {
                    s.loadType = AudioClipLoadType.DecompressOnLoad;
                    s.compressionFormat = snappy ? AudioCompressionFormat.PCM : AudioCompressionFormat.Vorbis;
                    s.quality = 0.8f;
                    s.preloadAudioData = true;
                }
                var old = importer.defaultSampleSettings;
                bool changed = old.loadType != s.loadType || old.compressionFormat != s.compressionFormat
                               || !Mathf.Approximately(old.quality, s.quality) || old.preloadAudioData != s.preloadAudioData
                               || importer.loadInBackground != music || importer.forceToMono != (!music && !loop);
                if (changed)
                {
                    importer.defaultSampleSettings = s;
                    importer.loadInBackground = music;
                    importer.forceToMono = !music && !loop;
                    importer.SaveAndReimport();
                }
                result[name] = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            }
            return result;
        }

        // ---------------- 라이브러리 ----------------

        private static SoundLibrary BuildLibrary(Dictionary<string, AudioClip> clips)
        {
            var library = AssetDatabase.LoadAssetAtPath<SoundLibrary>(LibraryPath);
            if (library == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LibraryPath));
                library = ScriptableObject.CreateInstance<SoundLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }
            var so = new SerializedObject(library);
            foreach (var pair in Map)
            {
                var found = new List<AudioClip>();
                foreach (var pattern in pair.Value)
                {
                    bool prefix = pattern.EndsWith("_");
                    found.AddRange(clips.Where(c => prefix ? c.Key.StartsWith(pattern) : c.Key == pattern)
                        .OrderBy(c => c.Key).Select(c => c.Value));
                }
                var arr = so.FindProperty(pair.Key + ".Clips");
                if (arr == null)
                {
                    Debug.LogWarning($"[AudioSetup] SoundLibrary.{pair.Key} 없음");
                    continue;
                }
                if (found.Count == 0)
                {
                    // 파일이 없으면 기존 연결을 지우지 않음 (2026-10-06: Audio/Build가 .gitignore의 **/[Bb]uild/에 걸려 이 PC에 없어,
                    // 실행할 때마다 건설 소리 연결이 비워졌음)
                    Debug.LogWarning($"[AudioSetup] {pair.Key}: 파일 없음 ({string.Join(", ", pair.Value)}) — 기존 연결 유지");
                    continue;
                }
                arr.arraySize = found.Count;
                for (int i = 0; i < found.Count; i++)
                    arr.GetArrayElementAtIndex(i).objectReferenceValue = found[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            return library;
        }

        // ---------------- 씬 ----------------

        private static void WireScene(SoundLibrary library)
        {
            var go = GameObject.Find("Audio");
            if (go == null)
                go = new GameObject("Audio");
            var service = GetOrAdd<AudioService>(go);
            Set(service, "_library", library);
            var game = GetOrAdd<GameAudio>(go);
            Set(game, "_station", Object.FindFirstObjectByType<StationController>());
            Set(game, "_build", Object.FindFirstObjectByType<BuildController>());
            Set(game, "_selection", Object.FindFirstObjectByType<ModuleSelectionController>());
            Set(game, "_actions", Object.FindFirstObjectByType<SelectionActionsPanel>());
            Set(game, "_clock", Object.FindFirstObjectByType<SimulationClock>());
            Set(game, "_meteors", Object.FindFirstObjectByType<MeteorFx>());
            Set(game, "_storm", Object.FindFirstObjectByType<SolarStormFx>());
            Set(game, "_camera", Camera.main);
            EditorSceneManager.MarkSceneDirty(go.scene);
        }

        private static void AddButtonSounds()
        {
            var hud = GameObject.Find("HUD");
            if (hud != null)
            {
                foreach (var b in hud.GetComponentsInChildren<Button>(true))
                {
                    var sound = GetOrAdd<UiSound>(b.gameObject);
                    // 결과에 전용 소리가 있는 버튼은 클릭음 생략 (배속·일시정지)
                    bool silent = b.GetComponentInParent<TimeControlPanel>(true) != null
                                  || b.GetComponent<BuildButtonView>() != null || b.GetComponent<BuildTabView>() != null;
                    sound.Mode = silent ? UiSound.ClickMode.Silent : UiSound.ClickMode.Click;
                    EditorUtility.SetDirty(sound);
                }
                EditorSceneManager.MarkSceneDirty(hud.scene);
            }
            AddToPrefab(ButtonPrefab);
            AddToPrefab(TabPrefab);
        }

        private static void AddToPrefab(string path)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var sound = GetOrAdd<UiSound>(root);
                sound.Mode = UiSound.ClickMode.Silent; // 건설 선택음·탭 전환음이 따로 난다
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // GetComponent의 "가짜 null"은 ??로 걸러지지 않으므로 명시적으로 비교
        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        private static void Set(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null)
            {
                Debug.LogWarning($"[AudioSetup] {target.GetType().Name}.{field} 없음");
                return;
            }
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
