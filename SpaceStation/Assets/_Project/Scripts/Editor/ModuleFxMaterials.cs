using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 5-5 모듈 연출 재질 (Art/Materials/FX). FBX 재질 슬롯 "FX_{이름}"은 M_{이름}에 연결된다.
    /// - M_ShieldShell: 반투명 외피 (SpaceStation/ShieldShell)
    /// - M_ShieldCore / M_ShieldOrb: HDR 단색 (블룸으로 빛남)
    /// - M_ShieldTrail: 빛점 꼬리 (가산 반투명 파티클)
    /// - M_FarmGlass: 수경 농장 온실 유리 (반투명 Lit)
    /// - M_FarmGrow: 수경 농장 생장등 (불투명 Lit + HDR 발광)
    /// - M_FusionPlasma / M_RefineryGlow: 핵융합로 플라스마, 제련소 용광로 불빛 (불투명 Lit + HDR 발광)
    /// </summary>
    public static class ModuleFxMaterials
    {
        public const string SlotPrefix = "FX_";
        private const string Folder = "Assets/_Project/Art/Materials/FX";

        public static Material Get(string name)
        {
            switch (name)
            {
                case "ShieldShell": return ShieldShell();
                case "ShieldCore": return Unlit("M_ShieldCore", new Color(1.4f, 3.0f, 5.0f, 1f));
                case "ShieldOrb": return Unlit("M_ShieldOrb", new Color(2.2f, 4.2f, 6.0f, 1f));
                case "ShieldTrail": return Additive("M_ShieldTrail", new Color(0.6f, 1.8f, 4.2f, 1f));
                case "ConnectorFlow": return ConnectorFlow();
                // 5-7 이벤트·배경 연출
                case "MeteorGlow": return Additive("M_MeteorGlow", new Color(2.4f, 0.9f, 0.25f, 0.6f), soft: false);
                case "MeteorTrail": return Additive("M_MeteorTrail", new Color(2.0f, 0.7f, 0.2f, 1f));
                case "MeteorFlash": return Additive("M_MeteorFlash", new Color(2.5f, 1.2f, 0.4f, 1f), soft: false);
                case "TurretLaser": return Additive("M_TurretLaser", new Color(4f, 0.35f, 0.3f, 1f));
                case "Spark": return Additive("M_Spark", new Color(2.2f, 1.4f, 0.7f, 1f));
                case "StormSpark": return Additive("M_StormSpark", new Color(1.6f, 2.2f, 4f, 1f));
                case "Dust": return Additive("M_Dust", new Color(0.9f, 0.95f, 1.1f, 1f));
                case "ShipEngine": return Additive("M_ShipEngine", new Color(0.6f, 1.6f, 3.5f, 1f));
                case "ShieldRipple": return ShieldRipple();
                // 유리 투명도 (사용자 2026-10-27 "유리가 있다는 정도만 인식되면"): 거의 투명 — 유리라는 건 하늘 반사 · 해 반짝임 · 비스듬한 가장자리가 알려 줌.
                //   측정 (물류 허브 지붕, 유리 없을 때 밝기 0.17): 예전 알파 0.12 + 발광 → 0.36 (회색 막). 바탕색은 센 햇빛을 여러 겹 받아 +0.14,
                //   발광 +0.03, 하늘 반사 +0.05 → 바탕색 알파 0.01~0.03 · 발광 0 · 하늘 반사 0.5~0.7
                case "FarmGlass": return Glass("M_FarmGlass", new Color(0.62f, 1f, 0.84f, 0.03f), Color.black, 0.92f, 0.7f);
                case "EvoGlass": return Glass("M_EvoGlass", new Color(0.82f, 0.86f, 1f, 0.015f), Color.black, 0.92f, 0.6f); // 진화 모듈 공통 유리 (흰 패널 · 유리 = 고급 재질, 사용자 결정 2026-10-22)
                case "FarmGrow": return Emissive("M_FarmGrow", new Color(0.35f, 0.9f, 0.4f, 1f), new Color(0.5f, 2.6f, 0.7f, 1f));
                case "FusionPlasma": return Emissive("M_FusionPlasma", new Color(0.3f, 0.75f, 1f, 1f), new Color(0.3f, 1.3f, 3.0f, 1f));
                case "RefineryGlow": return Emissive("M_RefineryGlow", new Color(0.9f, 0.22f, 0.03f, 1f), new Color(1.9f, 0.26f, 0.02f, 1f)); // 초록 성분이 크면 톤매핑 뒤 노랗게 보임
                case "ControlLamp": return Emissive("M_ControlLamp", new Color(1f, 0.55f, 0.08f, 1f), new Color(2.0f, 0.6f, 0.04f, 1f)); // 8-3 경광등·상황창
                case "FuelCellGlow": return Emissive("M_FuelCellGlow", new Color(0.2f, 0.9f, 0.8f, 1f), new Color(0.15f, 1.5f, 1.35f, 1f)); // 8-5 연료전지 (보조 발전 중에만 켜짐)
                case "CargoLights": return Emissive("M_CargoLights", new Color(0.5f, 1f, 0.4f, 1f), new Color(0.6f, 2.2f, 0.4f, 1f)); // 8-5 화물 터미널 유도등
                case "TelescopeLens": return Lens(); // 12-5 학교 천문대 망원경 렌즈
                // 12-5 모듈 상징 마크 = 형광 (사용자 제안 2026-10-14: 강한 발광은 번짐 · 하얗게 날아감 → 채도 높은 색이 조명과 무관하게 자기 밝기, 색 하나로 조절)
                case "FluorCyan": return Fluorescent("M_FluorCyan", new Color(0.25f, 1f, 1f));    // 물 추출기 물방울
                case "FluorWhite": return Fluorescent("M_FluorWhite", new Color(1f, 1f, 0.94f));  // 단백질 배양실 PROTEIN 글자
                // 기존 모듈 상징 마크 (emblem_builder): 산소 O₂ = Cyan, 물 재활용 · 연료전지 H₂ = Teal, 배터리 번개 · 정비 렌치 · 장갑 방패 = Yellow,
                // 거주 집 · 창고 상자 = Orange, 채굴 곡괭이 · 연구 원자 = Purple, 화물 착륙 H = Green, 포탑 조준선 = Red
                case "FluorTeal": return Fluorescent("M_FluorTeal", new Color(0.15f, 1f, 0.75f));
                case "FluorYellow": return Fluorescent("M_FluorYellow", new Color(1f, 0.92f, 0.15f));
                case "FluorOrange": return Fluorescent("M_FluorOrange", new Color(1f, 0.55f, 0.12f));
                case "FluorPurple": return Fluorescent("M_FluorPurple", new Color(0.78f, 0.45f, 1f));
                case "FluorGreen": return Fluorescent("M_FluorGreen", new Color(0.4f, 1f, 0.35f));
                case "FluorRed": return Fluorescent("M_FluorRed", new Color(1f, 0.25f, 0.22f));
                case "FluorPink": return Fluorescent("M_FluorPink", new Color(1f, 0.45f, 0.75f)); // 진화 복지 센터 십자 · 돌봄 고리 (의료실 분홍 십자)
                case "IceChunk": return Glass("M_IceChunk", new Color(0.78f, 0.93f, 1f, 0.55f), new Color(0.08f, 0.16f, 0.24f)); // 12-5 물 추출기 투입 얼음 (반투명)
                case "WaterColumn": return Emissive("M_WaterColumn", new Color(0.3f, 0.65f, 1f, 1f), new Color(0.35f, 1.1f, 2.6f, 1f)); // 12-5 물 추출기 응축탑 물기둥
                case "ServerLights": return Emissive("M_ServerLights", new Color(0.55f, 0.85f, 1f, 1f), new Color(0.5f, 1.4f, 2.4f, 1f)); // 12-5 데이터 센터 서버 랙 표시등
                case "ProteinCulture": return Emissive("M_ProteinCulture", new Color(1f, 0.78f, 0.42f, 1f), new Color(1.6f, 0.95f, 0.35f, 1f)); // 12-5 단백질 배양실 배양액 (크림빛 호박색)
                case "Rock": return Rock();
                case "ShieldSail": return Glass("M_ShieldSail", new Color(0.45f, 0.7f, 1f, 0.04f), new Color(0.01f, 0.03f, 0.07f), 0.92f, 0.7f); // 진화 실드 매트릭스 육각 격자 돔 유리 (하늘빛 — 실드라 아주 옅은 빛은 남김)
                case "TwinkleWhite": return Twinkle("M_TwinkleWhite", new Color(1.4f, 1.4f, 1.5f, 1f));   // 진화 복지 · 문화 센터 별빛 점 (점마다 따로 반짝임)
                case "TwinklePurple": return Twinkle("M_TwinklePurple", new Color(1.0f, 0.55f, 1.4f, 1f));
                // 진화 2단계 (미드나잇 몸체 · 여가 테마파크, 2026-10-26) — 값은 Blender evo_builder 재질과 맞춤
                case "FluorIce": return Fluorescent("M_FluorIce", new Color(0.62f, 0.93f, 1f));   // 테두리 띠 홈 안 청백 빛줄 2줄
                case "EvoGlassTeal": return Glass("M_EvoGlassTeal", new Color(0.5f, 0.9f, 0.93f, 0.025f), Color.black, 0.92f, 0.6f); // 넓은 옅은 청록 유리
                case "EvoGlassClear": return Glass("M_EvoGlassClear", new Color(0.62f, 0.9f, 0.95f, 0.012f), Color.black, 0.92f, 0.5f); // 안이 잘 보여야 하는 맑은 유리 (돔 · 드럼 바닥)
                case "AuroraGreen": return Glow("M_AuroraGreen", new Color(0.15f, 0.5f, 0.3f, 1f));     // 실드 돔 오로라 커튼 (겹친 주름이 더해져 밝아지므로 한 겹은 옅게)
                case "AuroraViolet": return Glow("M_AuroraViolet", new Color(0.36f, 0.22f, 0.55f, 1f)); // 오로라 · 은하수 빛 띠
                case "LightBeam": return Glow("M_LightBeam", new Color(0.1f, 0.18f, 0.2f, 1f));         // 등대 빛줄기 · 거울 → 수신기 빛 (옅게)
                case "GalaxyWater": return GalaxyWater();                                                // 은하수 물 (차폐벽 윗면 · 후룸 물길)
                case "SplashMist": return Glass("M_SplashMist", new Color(0.82f, 0.92f, 1f, 0.42f), new Color(0.22f, 0.27f, 0.32f), 0.3f); // 큰 낙하 물안개 (윤기 없이)
                case "Mirror": return Mirror();                                                          // 궤도 미러 해바라기 거울
                case "AuroraBarrier": return AuroraBarrier();                                            // 실드 돔이 운석을 막을 때 오로라 커튼 장벽
                case "FireworkSpark": return Additive("M_FireworkSpark", new Color(1.6f, 1.6f, 1.6f, 1f));  // 요새 포대 불꽃 빛 점 (색은 입자 색)
                case "FireworkLine": return FireworkLine();                                              // 별자리 잇는 빛줄 · 불꽃 꼬리
                // 2단계 후속 (사용자 2026-10-27): 모델마다 다른 재질은 StationArtBuilder.FxOverrides가 슬롯에 연결
                case "PlasmaBlue": return Plasma("M_PlasmaBlue", new Color(0.9f, 1.6f, 2.6f, 1f), new Color(0.35f, 0.45f, 1.8f, 1f));   // 융합 발전소 (파랑)
                case "PlasmaViolet": return Plasma("M_PlasmaViolet", new Color(1.7f, 1.1f, 2.6f, 1f), new Color(0.9f, 0.3f, 1.8f, 1f)); // 융합 플랜트 (보라)
                case "AuroraCurtainGreen": return AuroraCurtain("M_AuroraCurtainGreen", new Color(0.4f, 1.4f, 0.8f, 1f), new Color(0.25f, 0.8f, 0.75f, 1f), 0.33f);
                case "AuroraCurtainViolet": return AuroraCurtain("M_AuroraCurtainViolet", new Color(0.45f, 0.75f, 1.3f, 1f), new Color(0.7f, 0.3f, 1.2f, 1f), 0.43f);
                // 궤도 방어망 긴 커튼 (UV 모드, 3 · 4단계 반영 ② 2026-10-29): 초록 = 커튼 전체, 보라 = 위쪽 65% 겹 (Blender og_final _curtain_sheet)
                case "AuroraCurtainGreenUV": return AuroraCurtainUV("M_AuroraCurtainGreenUV", new Color(0.4f, 1.4f, 0.8f, 1f), new Color(0.25f, 0.8f, 0.75f, 1f), 0f);
                case "AuroraCurtainVioletUV": return AuroraCurtainUV("M_AuroraCurtainVioletUV", new Color(0.45f, 0.75f, 1.3f, 1f), new Color(0.7f, 0.3f, 1.2f, 1f), 0.35f);
                case "LightShaft": return CustomShader("M_LightShaft", "SpaceStation/LightShaft", new Color(1.5f, 1.3f, 0.85f, 1f));    // 궤도 미러 빛기둥 (LightBeamFx)
                case "Hidden": return Glow("M_Hidden", Color.clear);                                                                // 모델 연출을 코드 연출로 바꿀 때 슬롯을 비움
                case "Ripple": return Ripple();                                                                                     // 후룸 큰 낙하 물결 고리 (FlumeRideFx, 수평 입자)
                // 진화 3단계 (진주 · 금 몸체 · 살아 있는 행성 정원 + 빛과 수정, 2026-10-27) · 4단계 (우주 · 별빛, 2026-10-28) — 값은 Blender evo_builder
                //   (s3_fx_mats · glow_mat · RAINBOW)와 맞춤. 물 · 떨어지는 물 · 구름 · 빗줄기 · 무지개 · 수정은 몸체 텍스처에 굽지 않은 따로 재질 (사용자 2026-10-27)
                case "FluorGold": return Fluorescent("M_FluorGold", new Color(1f, 0.8f, 0.42f));   // 금빛 등불 · 창 · 꽃가루 · 금빛 신경
                //   ④ (2026-10-29) 움직임: 수정 = 도는 빛에 무지개빛 반짝, 물 = 잔물결 · 윤슬, 폭포 = 흘러내리는 물줄기, 무지개 = 빛 물결, 비 = 떨어지는 물방울
                case "Prism": return GlassMotion(Glass("M_Prism", new Color(0.86f, 0.96f, 1f, 0.22f), new Color(0.12f, 0.16f, 0.22f), 0.96f, 0.9f), "_GLINT", "_Glint"); // 수정 · 프리즘 (맑고 은은히 빛남)
                case "Rainbow0": case "Rainbow1": case "Rainbow2": case "Rainbow3": case "Rainbow4": case "Rainbow5": case "Rainbow6":
                    return Shimmer("M_" + name, Rainbow[name[name.Length - 1] - '0'] * 0.75f);                           // 무지개 띠 일곱 (가산 — 하늘에 비치는 빛)
                case "PureWater": return GlassMotion(Glass("M_PureWater", new Color(0.1f, 0.45f, 0.62f, 0.55f), new Color(0.01f, 0.04f, 0.06f), 0.95f, 0.8f), "_FLOW_POOL", "_FlowPool"); // 맑은 물 (호수 · 강 · 연못)
                case "Falls": return GlassMotion(Glass("M_Falls", new Color(0.72f, 0.88f, 0.96f, 0.6f), new Color(0.1f, 0.14f, 0.17f), 0.85f, 0.6f), "_FLOW_FALL", "_FlowFall"); // 떨어지는 물 (폭포)
                case "Cloud": return Cloud();                                                                                       // 구름 · 물안개
                case "Rain": return CustomShader("M_Rain", "SpaceStation/Rain", new Color(0.3f, 0.36f, 0.45f, 1f));               // 빗줄기 (가산, 옅게 — 물방울이 떨어짐)
                // ④ 흐르는 빛 (SpaceStation/PulseGlow — 형광 재질과 같은 바탕 · 발광, 모델 기준 한 점에서 퍼지거나 모여드는 빛 물결): StationArtBuilder.FxOverrides가 모델마다 연결
                //   AI 중추 빛 신경 = 양쪽 문에서 가운데 수정 뇌(1, −0.05, 0.5)로 모여듦 + 시냅스 깜빡임 + 뇌 둘레 맥박 (build_ai_core CX · YB · CZ)
                case "NeuralPurple": return PulseGlow("M_NeuralPurple", new Color(0.78f, 0.45f, 1f), new Vector3(1f, -0.05f, 0.5f), new Vector4(0.32f, -0.22f, 5f, 1.5f), 0.4f, new Vector4(1.2f, 0.9f, 0.18f, 0f), new Vector4(0.35f, 0.035f, 2.4f, 0f));
                case "NeuralGold": return PulseGlow("M_NeuralGold", new Color(1f, 0.8f, 0.42f), new Vector3(1f, -0.05f, 0.5f), new Vector4(0.32f, -0.22f, 5f, 1.5f), 0.4f, new Vector4(1.2f, 0.9f, 0.18f, 0f), new Vector4(0.35f, 0.035f, 2.4f, 0f));
                //   심우주 통신기지 신호 = 가운데 돔 위 신호 고리 셋 가운데(1, 0.175, 0.05)에서 퍼져 나감 → 고리가 안쪽부터 차례로 빛나고 호 · 케이블을 따라 번짐
                case "SignalCyan": return PulseGlow("M_SignalCyan", new Color(0.25f, 1f, 1f), new Vector3(1f, 0.175f, 0.05f), new Vector4(0.28f, 0.16f, 6f, 1.6f), 0.5f, Vector4.zero, Vector4.zero);
                //   항성 발전망 꿀 빛 = 꿀 언덕(5, 0, 0.5)에서 천천히 번지는 따스한 물결 + 칸마다 은은한 반짝
                case "HoneyGold": return PulseGlow("M_HoneyGold", new Color(1f, 0.8f, 0.42f), new Vector3(5f, 0f, 0.5f), new Vector4(0.9f, 0.3f, 3f, 0.8f), 0.6f, Vector4.zero, new Vector4(0.25f, 0.05f, 1.2f, 0f));
                //   궤도 항만 진주 빛 = 진주마다 따로 숨 쉬듯 (물결 없음)
                case "PearlWhite": return PulseGlow("M_PearlWhite", new Color(1f, 1f, 0.94f), Vector3.zero, Vector4.zero, 0.65f, Vector4.zero, new Vector4(0.7f, 0.035f, 1.3f, 0f));
                case "Beam": return Get("LightBeam");                                                                               // 궤도 방어망 등대 빛줄기 = 실드 돔 등대와 같은 재질
                case "Critters": return Textured("M_Critters", "Assets/_Project/Art/Models/Modules/Textures/Evo_Critters/T_Critters_BaseColor.png"); // 동물 공용 손그림 아틀라스
                case "Bee": return Textured("M_Bee", "Assets/_Project/Art/Models/Modules/Textures/Evo_Critters/T_Bee_BaseColor.png");             // 꿀벌 (얼굴 · 줄무늬)
                default: return null;
            }
        }

        /// <summary>Blender evo_builder RAINBOW (빨 · 주 · 노 · 초 · 파 · 남 · 보)와 같은 색.</summary>
        private static readonly Color[] Rainbow =
        {
            new Color(1f, 0.22f, 0.22f), new Color(1f, 0.58f, 0.12f), new Color(1f, 0.94f, 0.22f), new Color(0.3f, 1f, 0.38f),
            new Color(0.25f, 0.62f, 1f), new Color(0.42f, 0.32f, 1f), new Color(0.82f, 0.38f, 1f),
        };

        /// <summary>구름 · 물안개: 윤기 없는 흰 Lit + 약한 발광 (그늘에서도 하얗게).</summary>
        private static Material Cloud()
        {
            var m = Emissive("M_Cloud", new Color(0.95f, 0.96f, 0.99f, 1f), new Color(0.16f, 0.16f, 0.18f, 1f));
            m.SetFloat("_Smoothness", 0.1f);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>
        /// 손그림 텍스처 재질 (진화 3단계 동물 · 꿀벌 — 얼굴 · 무늬 = 텍스처, 사용자 2026-10-27): Lit + 같은 그림의 약한 발광
        /// (그늘 쪽 동물 얼굴도 읽히게). 텍스처가 없으면 null.
        /// </summary>
        private static Material Textured(string name, string texturePath)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (tex == null)
                return null;
            var m = Load(name, Shader.Find("Universal Render Pipeline/Lit"));
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", 0.3f);
            m.SetTexture("_EmissionMap", tex);
            m.SetKeyword(new LocalKeyword(m.shader, "_EMISSION"), true);
            m.SetColor("_EmissionColor", Color.white * 0.22f);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material ShieldShell()
        {
            var m = Load("M_ShieldShell", Shader.Find("SpaceStation/ShieldShell"));
            m.SetColor("_BaseColor", Color.white);
            // 가운데는 비치고 가장자리만 빛나게 (안쪽 코어·빛점이 보이도록)
            m.SetColor("_ShellColor", new Color(0.12f, 0.45f, 1.4f, 1f));
            m.SetFloat("_FillAlpha", 0.05f);
            m.SetFloat("_RimPower", 2.6f);
            m.SetFloat("_RimStrength", 1.5f);
            m.SetFloat("_WaveStrength", 0.28f);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>5-7 실드가 운석을 빗겨낼 때 범위 구면 파문 (알파는 MeteorFx가 _BaseColor로 페이드).</summary>
        private static Material ShieldRipple()
        {
            var m = Load("M_ShieldRipple", Shader.Find("SpaceStation/ShieldShell"));
            m.SetColor("_BaseColor", Color.white);
            m.SetColor("_ShellColor", new Color(0.3f, 1.0f, 2.6f, 1f));
            m.SetFloat("_FillAlpha", 0f);
            m.SetFloat("_RimPower", 4.5f);
            m.SetFloat("_RimStrength", 1.8f);
            m.SetFloat("_WaveStrength", 0.12f);
            m.SetFloat("_WaveScale", 3f);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>반투명 유리 (수경 농장 온실, 2026-10-02): URP Lit 투명 + 반사 + 은은한 발광 (밤에도 윤곽이 보이게).</summary>
        /// <summary>
        /// 반투명 유리: SpaceStation/Glass (양면, 뒷면은 법선을 뒤집어 앞면처럼 비춤 · 반사 보존). 2026-10-27 전에는 URP Lit 양면이었는데,
        /// 뒷면 법선이 시선 반대라 프레넬 최대로 하늘 반사가 겹쳐 유리에 뿌연 흰 뭉치(사용자 "멍울")가 생겼다.
        /// tint 알파 = 바탕색 진하기, reflection = 하늘 반사 · 주변광 세기 (해 반짝임은 그대로).
        /// </summary>
        private static Material Glass(string name, Color tint, Color emission, float smoothness = 0.92f, float reflection = 1f)
        {
            var m = Load(name, Shader.Find("SpaceStation/Glass"));
            m.SetColor("_BaseColor", tint);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", smoothness);
            m.SetColor("_EmissionColor", emission);
            m.SetFloat("_Reflection", reflection);
            m.shaderKeywords = System.Array.Empty<string>(); // URP Lit 시절 키워드 정리
            m.renderQueue = (int)RenderQueue.Transparent;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>
        /// 12-5 망원경 렌즈 (사용자 요청 2026-10-14): 반투명한 어두운 유리에 우주 배경이 비침 (SpaceStation/SkyLens).
        /// 환경 반사(128)는 별이 뭉개졌고, 은하 그림 고정은 각도와 무관했음 → 우주 배경을 512 큐브맵으로 구워 반사 방향으로 읽음.
        /// </summary>
        private const string SkyCubePath = "Assets/_Project/Art/Textures/T_SkyReflection.cubemap";

        private static Material Lens()
        {
            var m = Load("M_TelescopeLens", Shader.Find("SpaceStation/SkyLens"));
            m.SetColor("_BaseColor", Color.white);
            m.SetColor("_GlassColor", new Color(0.04f, 0.06f, 0.13f, 0.72f));
            m.SetTexture("_SkyCube", SkyCube());
            m.SetFloat("_SkyStrength", 1.6f);
            m.SetFloat("_FresnelPower", 3f);
            m.SetFloat("_SpecPower", 160f);
            m.SetFloat("_SpecStrength", 1.5f);
            m.renderQueue = (int)RenderQueue.Transparent;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>현재 씬 스카이박스를 512 큐브맵 에셋으로 굽는다 (없을 때만 · 스카이박스를 바꾸면 에셋을 지우고 다시).</summary>
        public static Cubemap SkyCube()
        {
            var cube = AssetDatabase.LoadAssetAtPath<Cubemap>(SkyCubePath);
            if (cube != null)
                return cube;
            if (RenderSettings.skybox == null)
            {
                Debug.LogWarning("[ModuleFxMaterials] 스카이박스가 없어 렌즈 큐브맵을 굽지 못함 (게임 씬을 열고 다시)");
                return null;
            }
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SkyCubePath));
            var go = new GameObject("_SkyCubeCam") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.cullingMask = 0; // 하늘만
                cam.clearFlags = CameraClearFlags.Skybox;
                cube = new Cubemap(512, TextureFormat.RGBA32, true);
                cam.RenderToCubemap(cube);
                cube.Apply(true);
                AssetDatabase.CreateAsset(cube, SkyCubePath);
                return cube;
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// 12-5 형광 마크: 발광 = 색 × 0.85 (블룸 문턱 아래 → 번짐 없이 색 그대로 또렷, 그늘 · 밤에도 같은 밝기).
        /// Lit + 발광이라 ModuleView가 비활성 · 파손 상태에서 어둡게 / 깜빡이게 조절한다.
        /// </summary>
        private static Material Fluorescent(string name, Color color)
        {
            // 바탕(조명 반응)은 어둡게, 색은 발광으로 → 조명 + 발광이 더해져 하얗게 바래지 않음 (처음 바탕 1 + 발광 1.05는 노랑이 크림색으로 보였음)
            var m = Emissive(name, color * 0.3f, color * 0.85f);
            m.SetFloat("_Smoothness", 0.35f);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>
        /// 불투명 발광체 (7-4 수경 농장 생장등): URP Lit + HDR 발광 → 블룸으로 빛남.
        /// Lit이라 ModuleView가 발광 세기를 상태별로 조절한다 (비활성 소등, 파손 깜빡임).
        /// </summary>
        private static Material Emissive(string name, Color baseColor, Color hdrEmission)
        {
            var m = Load(name, Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", baseColor);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", 0.6f);
            m.SetKeyword(new LocalKeyword(m.shader, "_EMISSION"), true);
            m.SetColor("_EmissionColor", hdrEmission);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>
        /// 진화 2단계 빛 막 (오로라 커튼 · 빛줄기): URP Unlit 가산 + 양면. 모델 FX 메시는 꼭짓점 색이 검정(반짝임 위상용)이라
        /// 꼭짓점 색을 곱하는 Particles/Unlit은 안 보임 → 꼭짓점 색을 안 쓰는 Unlit. ModuleView가 _BaseColor로 비활성 때 어둡게.
        /// </summary>
        private static Material Glow(string name, Color hdr)
        {
            var m = Load(name, Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetColor("_BaseColor", hdr);
            m.SetFloat("_Surface", 1f);   // Transparent
            m.SetFloat("_Blend", 2f);     // Additive
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.One);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", (float)CullMode.Off);
            m.SetKeyword(new LocalKeyword(m.shader, "_SURFACE_TYPE_TRANSPARENT"), true);
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetOverrideTag("RenderType", "Transparent");
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>
        /// 은하수 물 (SpaceStation/GalaxyWater, 사용자 2026-10-27 "진짜 은하수처럼 반짝 · 생동감 있는 물"): 깊은 남청 반투명 위로
        /// 흐르는 물결 반사광 · 저마다 반짝이는 별 · 비스듬할수록 밝은 물빛. 속 빛 띠 · 별가루가 묻히지 않게 물을 먼저 그림.
        /// </summary>
        private static Material GalaxyWater()
        {
            var m = Load("M_GalaxyWater", UnityEngine.Shader.Find("SpaceStation/GalaxyWater"));
            m.SetColor("_BaseColor", Color.white);
            m.SetColor("_DeepColor", new Color(0.03f, 0.08f, 0.3f, 0.72f));
            m.renderQueue = (int)RenderQueue.Transparent - 1;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>플라즈마 고리 (SpaceStation/Plasma): 흐르는 소용돌이 · 빛 실 · 맥동, 가산.</summary>
        private static Material Plasma(string name, Color core, Color edge)
        {
            var m = Load(name, UnityEngine.Shader.Find("SpaceStation/Plasma"));
            m.SetColor("_BaseColor", Color.white);
            m.SetColor("_CoreColor", core);
            m.SetColor("_EdgeColor", edge);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>실드 돔 오로라 커튼 (SpaceStation/AuroraCurtain): 등대(모델 0.4, 0.6) 둘레로 일렁이고 아랫단이 밝고 위로 옅어짐. 커튼 아래 높이 = baseY.</summary>
        private static Material AuroraCurtain(string name, Color low, Color high, float baseY)
        {
            var m = Load(name, UnityEngine.Shader.Find("SpaceStation/AuroraCurtain"));
            m.SetColor("_BaseColor", Color.white);
            m.SetColor("_LowColor", low);
            m.SetColor("_HighColor", high);
            m.SetVector("_Center", new Vector4(0.4f, 0f, 0.6f, 0f));
            m.SetFloat("_BaseY", baseY);
            m.SetFloat("_Height", 0.36f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>궤도 방어망 긴 커튼 (AuroraCurtain UV 모드): u = 길이, v = 아랫단 → 위, 커튼 면 수직 = 접선 × 위. vStart = 이 겹이 시작하는 v.</summary>
        private static Material AuroraCurtainUV(string name, Color low, Color high, float vStart)
        {
            var m = Load(name, UnityEngine.Shader.Find("SpaceStation/AuroraCurtain"));
            m.SetColor("_BaseColor", Color.white);
            m.SetColor("_LowColor", low);
            m.SetColor("_HighColor", high);
            m.SetFloat("_UVMode", 1f);
            m.EnableKeyword("_UVMODE");
            m.SetFloat("_Length", 2.5f);
            m.SetFloat("_VStart", vStart);
            m.SetFloat("_Sway", 0.035f);
            m.SetFloat("_Intensity", 0.48f); // 초록 · 보라 두 겹이 겹치는 띠가 하얗게 날지 않게 실드 돔(0.6)보다 옅게
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>유리 셰이더 움직임 켜기 (_FLOW_FALL 폭포 · _FLOW_POOL 물결 · _GLINT 수정 반짝) — Glass()가 키워드를 비우므로 그 뒤에.</summary>
        private static Material GlassMotion(Material m, string keyword, string toggle)
        {
            m.SetVector("_Flow", new Vector4(14f, 0.9f, 0.02f, 0.5f));
            m.SetVector("_Ripple", new Vector4(70f, 1.0f, 0.1f, 0.18f)); // 해를 받으면 윤슬이 빽빽한 별밭처럼 보여 흔들림 · 윤슬을 옅게
            m.SetVector("_GlintParams", new Vector4(1.2f, 0.35f, 24f, 0.7f));
            m.SetFloat(toggle, 1f);
            m.EnableKeyword(keyword);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>무지개 띠 빛 흐름 (SpaceStation/Shimmer): 가산 · 양면, 밝은 물결이 비스듬히 지나감.</summary>
        private static Material Shimmer(string name, Color color)
        {
            var m = Load(name, UnityEngine.Shader.Find("SpaceStation/Shimmer"));
            m.SetColor("_BaseColor", color);
            m.renderQueue = (int)RenderQueue.Transparent;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>
        /// 흐르는 빛 (SpaceStation/PulseGlow): 형광 재질(Fluorescent)과 같은 바탕 (색 × 0.3) · 발광 (색 × 0.85)에 빛 물결 · 맥박 · 칸 반짝을 곱함.
        /// wave = 파장 · 속도 (+ 퍼짐, − 모임) · 날카로움 · 세기, beat = 세기 · 초당 · 반지름, spark = 세기 · 칸 크기 · 빠르기.
        /// </summary>
        private static Material PulseGlow(string name, Color color, Vector3 center, Vector4 wave, float floor, Vector4 beat, Vector4 spark)
        {
            var m = Load(name, UnityEngine.Shader.Find("SpaceStation/PulseGlow"));
            m.SetColor("_BaseColor", color * 0.3f);
            m.SetColor("_EmissionColor", color * 0.85f);
            m.SetFloat("_Smoothness", 0.35f);
            m.SetVector("_Center", center);
            m.SetVector("_Wave", wave);
            m.SetFloat("_Floor", floor);
            m.SetVector("_Beat", beat);
            m.SetVector("_Spark", spark);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>_BaseColor 하나만 정하는 커스텀 셰이더 재질.</summary>
        private static Material CustomShader(string name, string shader, Color color)
        {
            var m = Load(name, UnityEngine.Shader.Find(shader));
            m.SetColor("_BaseColor", color);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>오로라 커튼 장벽 (AuroraBarrierFx): 꼭짓점 색(초록 → 보라 · 알파)을 곱하는 가산 파티클 재질, 양면. 세기는 HDR 색으로.</summary>
        private static Material AuroraBarrier()
        {
            var m = Additive("M_AuroraBarrier", new Color(0.9f, 0.9f, 0.9f, 1f), soft: false); // 더 세면 블룸에 하얗게 날아가 초록 · 보라가 안 보임
            m.SetFloat("_Cull", (float)CullMode.Off);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>
        /// 불꽃 빛줄 (FireworkFx 별자리 선 · 입자 꼬리): 가로질러서만 부드러운 띠 텍스처 — 둥근 점 텍스처는 선 길이 방향으로 늘어나 양끝이 어두워짐.
        /// 불꽃놀이 팩(NamuFX, git 제외)의 Trail 텍스처와 같은 모양을 직접 만듦 (팩이 없는 PC에서도 동작).
        /// </summary>
        private static Material FireworkLine()
        {
            var m = Additive("M_FireworkLine", new Color(1.3f, 1.3f, 1.4f, 1f), soft: false);
            m.SetTexture("_BaseMap", SoftBand());
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>가로질러서만 부드러운 띠 (v 방향 가운데가 밝음, u 방향은 고름). 없으면 만든다.</summary>
        private static Texture2D SoftBand()
        {
            string path = $"{Folder}/T_SoftBand.png";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null)
                return tex;
            const int w = 8, h = 64;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int y = 0; y < h; y++)
            {
                float d = Mathf.Abs((y + 0.5f) / h * 2f - 1f);
                float a = Mathf.Clamp01(1f - d);
                a = a * a * (3f - 2f * a);
                for (int x = 0; x < w; x++)
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            t.Apply();
            System.IO.File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter ti)
            {
                ti.alphaIsTransparency = true;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.mipmapEnabled = true;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>물결 고리 (FlumeRideFx 큰 낙하 물보라): 가산 입자 + 가장자리만 밝은 고리 텍스처 — 수평 입자가 커지며 퍼짐.</summary>
        private static Material Ripple()
        {
            var m = Additive("M_Ripple", new Color(1.1f, 1.25f, 1.5f, 1f), soft: false);
            m.SetTexture("_BaseMap", SoftRing());
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>부드러운 고리 텍스처 (반지름 0.8 둘레만 밝음). 없으면 만든다.</summary>
        private static Texture2D SoftRing()
        {
            string path = $"{Folder}/T_SoftRing.png";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null)
                return tex;
            const int size = 128;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                    float a = Mathf.Clamp01(1f - Mathf.Abs(Mathf.Sqrt(dx * dx + dy * dy) - 0.8f) / 0.14f);
                    a = a * a * (3f - 2f * a);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            t.Apply();
            System.IO.File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter ti)
            {
                ti.alphaIsTransparency = true;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.mipmapEnabled = true;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>해바라기 거울: 하늘을 비추는 은빛 금속 (불투명 Lit, 그늘에서도 윤곽이 보이게 약한 발광).</summary>
        private static Material Mirror()
        {
            var m = Emissive("M_Mirror", new Color(0.78f, 0.88f, 0.98f, 1f), new Color(0.04f, 0.06f, 0.09f));
            m.SetFloat("_Metallic", 0.9f);
            m.SetFloat("_Smoothness", 0.9f);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>5-7 운석·배경 소행성 바위.</summary>
        private static Material Rock()
        {
            var m = Load("M_Rock", Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", new Color(0.24f, 0.21f, 0.19f, 1f));
            m.SetFloat("_Metallic", 0.05f);
            m.SetFloat("_Smoothness", 0.15f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>5-6 연결 통로 불빛 띠 (코어에서 바깥으로 흐름).</summary>
        private static Material ConnectorFlow()
        {
            var m = Load("M_ConnectorFlow", Shader.Find("SpaceStation/ConnectorFlow"));
            m.SetColor("_BaseColor", new Color(0.25f, 1.1f, 2.6f, 1f));
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>별빛 점: 꼭짓점 색 R(점마다 무작위 위상)로 점마다 다른 박자로 반짝임 — 밝기만 바뀜.</summary>
        private static Material Twinkle(string name, Color hdr)
        {
            var m = Load(name, Shader.Find("SpaceStation/Twinkle"));
            m.SetColor("_BaseColor", hdr);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material Unlit(string name, Color hdr)
        {
            var m = Load(name, Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetColor("_BaseColor", hdr);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>둥근 부드러운 점 텍스처 (입자·꼬리가 네모로 보이지 않도록). 없으면 만든다.</summary>
        private static Texture2D SoftDot()
        {
            string path = $"{Folder}/T_SoftDot.png";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null)
                return tex;
            const int size = 64;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                    float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    a = a * a * (3f - 2f * a); // 부드러운 가장자리
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            t.Apply();
            System.IO.File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter ti)
            {
                ti.alphaIsTransparency = true;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.mipmapEnabled = true;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Material Additive(string name, Color hdr, bool soft = true)
        {
            var m = Load(name, Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            // 메시(구·바위)에 쓰는 재질은 UV가 없거나 의미가 없으므로 텍스처 없이
            m.SetTexture("_BaseMap", soft ? SoftDot() : null);
            m.SetFloat("_Surface", 1f);    // Transparent
            m.SetFloat("_Blend", 2f);      // Additive
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.One);
            m.SetFloat("_ZWrite", 0f);
            m.SetKeyword(new LocalKeyword(m.shader, "_SURFACE_TYPE_TRANSPARENT"), true);
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetColor("_BaseColor", hdr);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material Load(string name, Shader shader)
        {
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/_Project/Art/Materials", "FX");
            string path = $"{Folder}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != shader)
            {
                m.shader = shader;
            }
            return m;
        }
    }
}
