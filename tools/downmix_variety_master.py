"""
Muse Dash Downmix Master Pipeline
건반 BMS (식스타 게이트 등) -> 뮤즈 대시 2레인 BMS (hwa2.bms) 오가닉 다운믹스 엔진
- 기계적 1:1 지그재그 억제 & 프레이즈 클러스터링 기반 2~3연타 손맛(Same-lane ~58%) 보장
- 타격 비트 내 복선(톱니바퀴 회피 장애물) 완전 배제
- 마디 및 프레이즈 단위 방향 삼각형(Normal / Up / Down) 일관성 유지
- 12~16마디 이상 롱스팬(Long-span) 악절 단위 몬스터 에셋 군단 지속성 유지
- 클라이맥스/아웃트로 구간 다른 씬(예: 2번 씬) 몬스터 전환 연출 지원
"""

import os
import re
from collections import defaultdict, Counter

template_path = os.path.join(r"H:\muse dash hwa\hwa", "il vento d'oro", "hwa2.bms")
with open(template_path, 'r', encoding='utf-8', errors='ignore') as f:
    template_lines = f.readlines()
wav_lines = [l.rstrip() for l in template_lines if l.strip().startswith('#WAV')]

# Rich Scene 6 Speed 2 Palette (하얀 그리움)
SCENE_6 = {
    # Small Note 1 (Normal, Up, Down)
    'S1_G': ['0QD', '0QF', '0QH'],
    'S1_A': ['0QE', '0QG', '0QI'],
    # Small Note 2 (Normal, Up, Down)
    'S2_G': ['0QJ', '0QL', '0QN'],
    'S2_A': ['0QK', '0QM', '0QO'],
    # Small Note 3 (Normal, Up, Down)
    'S3_G': ['0QP', '0QR', '0QT'],
    'S3_A': ['0QQ', '0QS', '0QU'],
    # Big Notes 1 & 2
    'B1_G': '0QV', 'B1_A': '0QW',
    'B2_G': '0QX', 'B2_A': '0QY',
    # Special Objects
    'HAMMER_G': '0QZ', 'HAMMER_A': '0R0',
    'RIDER_G':  '0R1', 'RIDER_A':  '0R2',
    'GHOST_G':  '0R3', 'GHOST_A':  '0R4',
    'GEAR_G':   '0PZ', 'GEAR_A':   '0Q0',
    # Holds
    'HOLD_S_G': '0PW', 'HOLD_E_G': '0PV',
    'HOLD_S_A': '0PY', 'HOLD_E_A': '0PX',
    # Boss
    'BOSS_APP': '082', 'BOSS_DIS': '083',
    'BOSS_A1_S': '084', 'BOSS_A1_E': '085',
    'BOSS_A2_S': '086', 'BOSS_A2_E': '087',
    'BOSS_P1_G': '0Q1', 'BOSS_P1_A': '0Q3',
    'BOSS_P2_G': '0Q5', 'BOSS_P2_A': '0Q7'
}

# Rich Scene 4 Speed 2 Palette (미소의 세상 너에게만)
SCENE_4 = {
    # Small Note 1 (Normal, Up, Down)
    'S1_G': ['0N9', '0NB', '0ND'],
    'S1_A': ['0NA', '0NC', '0NE'],
    # Small Note 2 (Normal, Up, Down)
    'S2_G': ['0NF', '0NH', '0NJ'],
    'S2_A': ['0NG', '0NI', '0NK'],
    # Small Note 3 (Normal, Up, Down)
    'S3_G': ['0NL', '0NN', '0NP'],
    'S3_A': ['0NM', '0NO', '0NQ'],
    # Big Notes 1 & 2
    'B1_G': '0NR', 'B1_A': '0NS',
    'B2_G': '0NT', 'B2_A': '0NU',
    # Special Objects
    'HAMMER_G': '0NV', 'HAMMER_A': '0NW',
    'RIDER_G':  '0NX', 'RIDER_A':  '0NY',
    'GHOST_G':  '0NZ', 'GHOST_A':  '0O0',
    'GEAR_G':   '0MV', 'GEAR_A':   '0MW',
    # Holds
    'HOLD_S_G': '0MS', 'HOLD_E_G': '0MR',
    'HOLD_S_A': '0MU', 'HOLD_E_A': '0MT',
    # Boss
    'BOSS_APP': '04W', 'BOSS_DIS': '04X',
    'BOSS_A1_S': '04Y', 'BOSS_A1_E': '04Z',
    'BOSS_A2_S': '050', 'BOSS_A2_E': '051',
    'BOSS_P1_G': '0MX', 'BOSS_P1_A': '0MZ',
    'BOSS_P2_G': '0N1', 'BOSS_P2_A': '0N3'
}

# Rich Scene 2 Speed 2 Palette (우주/캔디 테마 - 하얀 그리움 115+ 마디 전환용)
SCENE_2 = {
    # Small Note 1 (Normal, Up, Down)
    'S1_G': ['0K5', '0K7', '0K9'],
    'S1_A': ['0K6', '0K8', '0KA'],
    # Small Note 2 (Normal, Up, Down)
    'S2_G': ['0KB', '0KD', '0KF'],
    'S2_A': ['0KC', '0KE', '0KG'],
    # Small Note 3 (Normal, Up, Down)
    'S3_G': ['0KH', '0KJ', '0KL'],
    'S3_A': ['0KI', '0KK', '0KM'],
    # Big Notes 1 & 2
    'B1_G': '0KN', 'B1_A': '0KO',
    'B2_G': '0KP', 'B2_A': '0KQ',
    # Special Objects
    'HAMMER_G': '0KR', 'HAMMER_A': '0KS',
    'RIDER_G':  '0KT', 'RIDER_A':  '0KU',
    'GHOST_G':  '0KV', 'GHOST_A':  '0KW',
    # Holds
    'HOLD_S_G': '0JO', 'HOLD_E_G': '0JN',
    'HOLD_S_A': '0JQ', 'HOLD_E_A': '0JP',
    # Boss (Fallback)
    'BOSS_APP': '082', 'BOSS_DIS': '083',
    'BOSS_A1_S': '084', 'BOSS_A1_E': '085',
    'BOSS_A2_S': '086', 'BOSS_A2_E': '087',
    'BOSS_P1_G': '0Q1', 'BOSS_P1_A': '0Q3',
    'BOSS_P2_G': '0Q5', 'BOSS_P2_A': '0Q7'
}

def build_downmix(sixtar_path, out_path, title, artist, bpm, palette_fn, boss_plan, verse_ranges, theme_fn):
    with open(sixtar_path, 'r', encoding='cp949', errors='ignore') as f:
        lines = f.readlines()

    meas_events = defaultdict(lambda: defaultdict(list))
    max_meas = 0

    for line in lines:
        line = line.strip()
        m_chan = re.match(r'^#(\d{3})([0-9A-Za-z]{2}):(.*)$', line)
        if not m_chan: continue
        m = int(m_chan.group(1))
        c = m_chan.group(2)
        d = m_chan.group(3).strip()
        if m > max_meas: max_meas = m
        chunk_size = 2
        total_chunks = len(d) // chunk_size
        for i in range(total_chunks):
            tok = d[i*chunk_size:(i+1)*chunk_size]
            if tok != '00':
                meas_events[m][i/total_chunks].append((c, tok))

    # 1. Holds handling
    channel_holds = defaultdict(list)
    active_starts = {}
    for m in sorted(meas_events.keys()):
        for f in sorted(meas_events[m].keys()):
            for c, tok in meas_events[m][f]:
                if tok == '02': active_starts[c] = (m, f)
                elif tok == '03' and c in active_starts:
                    s_m, s_f = active_starts.pop(c)
                    channel_holds[c].append((s_m, s_f, m, f))

    all_holds = []
    for c, hlist in channel_holds.items():
        for s_m, s_f, e_m, e_f in hlist:
            all_holds.append((s_m, s_f, e_m, e_f, c))
    all_holds.sort(key=lambda x: (x[0], x[1]))

    assigned_holds = []
    g_busy_until = (-1, -1.0)
    a_busy_until = (-1, -1.0)
    g_hold_count = 0
    a_hold_count = 0

    for s_m, s_f, e_m, e_f, c in all_holds:
        s_pos = s_m + s_f
        e_pos = e_m + e_f
        g_free = (s_pos >= g_busy_until[0] + g_busy_until[1])
        a_free = (s_pos >= a_busy_until[0] + a_busy_until[1])

        target = None
        if g_free and a_free:
            target = '13' if g_hold_count <= a_hold_count else '14'
        elif g_free: target = '13'
        elif a_free: target = '14'
        else: continue

        if target == '13':
            assigned_holds.append((s_m, s_f, e_m, e_f, '13'))
            g_busy_until = (e_m, e_f)
            g_hold_count += 1
        else:
            assigned_holds.append((s_m, s_f, e_m, e_f, '14'))
            a_busy_until = (e_m, e_f)
            a_hold_count += 1

    def is_lane_holding(lane, m, f):
        pos = m + f
        for s_m, s_f, e_m, e_f, t in assigned_holds:
            if t == lane:
                s_pos = s_m + s_f
                e_pos = e_m + e_f
                if s_pos < pos < e_pos: return 'inside'
                elif abs(pos - s_pos) < 1e-5: return 'start'
                elif abs(pos - e_pos) < 1e-5: return 'end'
        return 'free'

    md_timeline = defaultdict(lambda: defaultdict(dict))

    # Place holds
    for s_m, s_f, e_m, e_f, t in assigned_holds:
        pal = palette_fn(s_m)
        if t == '13':
            md_timeline[s_m][s_f]['13'] = pal['HOLD_S_G']
            md_timeline[e_m][e_f]['13'] = pal['HOLD_E_G']
        else:
            md_timeline[s_m][s_f]['14'] = pal['HOLD_S_A']
            md_timeline[e_m][e_f]['14'] = pal['HOLD_E_A']

    # 2. Boss Plan
    boss_app, act1_s, proj1_s, proj1_e, act1_e, act2_s, proj2_s, proj2_e, act2_e, boss_dis = boss_plan
    b_pal = palette_fn(boss_app)
    md_timeline[boss_app][0.0]['15'] = b_pal['BOSS_APP']
    md_timeline[act1_s][0.0]['18']   = b_pal['BOSS_A1_S']
    md_timeline[act1_e][0.0]['18']   = b_pal['BOSS_A1_E']
    md_timeline[act2_s][0.0]['18']   = b_pal['BOSS_A2_S']
    md_timeline[act2_e][0.0]['18']   = b_pal['BOSS_A2_E']
    md_timeline[boss_dis][0.0]['15'] = b_pal['BOSS_DIS']

    # 3. Dynamic Rhythmic Thinning & Phrase-based Lane Clustering
    ground_count = g_hold_count * 2
    air_count = a_hold_count * 2
    sim_count = 0
    last_lane = '13'
    last_tap_pos = -999.0
    lane_run = 0

    b_toggle = 0

    # Helper for diverse object picking
    def pick_diverse_object(lane, m, f, is_proj, is_proj1):
        nonlocal b_toggle
        pal = palette_fn(m)
        if is_proj:
            if lane == '13':
                return pal['BOSS_P1_G'] if is_proj1 else pal['BOSS_P2_G']
            else:
                return pal['BOSS_P1_A'] if is_proj1 else pal['BOSS_P2_A']

        is_downbeat = any(abs(f - b) < 1e-4 for b in (0.0, 0.5))

        # 1. Big Note (Matched with Section Theme!)
        theme = theme_fn(m)
        if is_downbeat and (m % 3 == 0 or (m % 4 == 2 and f == 0.0)):
            if theme == 0:
                return pal['B1_G'] if lane == '13' else pal['B1_A']
            elif theme == 1:
                return pal['B2_G'] if lane == '13' else pal['B2_A']
            else:
                b_toggle += 1
                if b_toggle % 2 == 1:
                    return pal['B1_G'] if lane == '13' else pal['B1_A']
                else:
                    return pal['B2_G'] if lane == '13' else pal['B2_A']

        # 2. Hammer on strong accented cadences / bar endings
        if (m % 4 == 3 and abs(f - 0.75) < 1e-4) or (m % 5 == 2 and abs(f - 0.5) < 1e-4):
            return pal['HAMMER_G'] if lane == '13' else pal['HAMMER_A']

        # 3. Rider on phrase openings or energetic downbeats
        if (m % 4 == 1 and abs(f - 0.0) < 1e-4) or (m % 6 == 4 and abs(f - 0.25) < 1e-4):
            return pal['RIDER_G'] if lane == '13' else pal['RIDER_A']

        # 4. Ghost on syncopated melodic notes
        if (m % 4 == 0 and abs(f - 0.25) < 1e-4) or (m % 5 == 3 and abs(f - 0.75) < 1e-4):
            return pal['GHOST_G'] if lane == '13' else pal['GHOST_A']

        # 5. Consistent Direction per Measure:
        m_cycle = m % 6
        if m_cycle == 2:
            motion_idx = 1  # Consistent Up (▲)
        elif m_cycle == 5:
            motion_idx = 2  # Consistent Down (▼)
        else:
            motion_idx = 0  # Clean Normal (No triangle)

        # 6. Varied Small Notes with LONG-SPAN Section Stability!
        if theme == 0:
            return pal['S1_G'][motion_idx] if lane == '13' else pal['S1_A'][motion_idx]
        elif theme == 1:
            return pal['S2_G'][motion_idx] if lane == '13' else pal['S2_A'][motion_idx]
        else:
            return pal['S3_G'][motion_idx] if lane == '13' else pal['S3_A'][motion_idx]

    for m in sorted(meas_events.keys()):
        is_verse = any(vs <= m <= ve for vs, ve in verse_ranges)
        is_proj1 = (proj1_s <= m <= proj1_e)
        is_proj2 = (proj2_s <= m <= proj2_e)
        is_boss_proj = is_proj1 or is_proj2

        # In each measure, define a rhythmic motif preference:
        motif_base = '13' if (m % 2 == 0) else '14'
        motif_alt  = '14' if motif_base == '13' else '13'

        for f in sorted(meas_events[m].keys()):
            pos = m + f
            dt = pos - last_tap_pos
            evs = meas_events[m][f]
            tap_keys = [e for e in evs if e[0] in ('11', '12', '13', '14', '15', '16') and e[1] == '01']

            if not tap_keys:
                continue

            # Gap filtering
            if is_verse:
                if dt < 0.18: continue
            elif not is_boss_proj:
                if dt < 0.12: continue
            else:
                if dt < 0.09: continue

            g_state = is_lane_holding('13', m, f)
            a_state = is_lane_holding('14', m, f)
            g_can = (g_state == 'free') and ('13' not in md_timeline[m][f])
            a_can = (a_state == 'free') and ('14' not in md_timeline[m][f])

            if not g_can and not a_can:
                continue

            num_taps = len(tap_keys)
            is_downbeat = any(abs(f - b) < 1e-4 for b in (0.0, 0.5))

            # Hold + Tap (One lane busy with hold)
            if g_can and not a_can:
                tok = pick_diverse_object('13', m, f, is_boss_proj, is_proj1)
                md_timeline[m][f]['13'] = tok
                ground_count += 1
                lane_run = (lane_run + 1) if last_lane == '13' else 1
                last_lane = '13'
                last_tap_pos = pos
                continue

            if a_can and not g_can:
                tok = pick_diverse_object('14', m, f, is_boss_proj, is_proj1)
                md_timeline[m][f]['14'] = tok
                air_count += 1
                lane_run = (lane_run + 1) if last_lane == '14' else 1
                last_lane = '14'
                last_tap_pos = pos
                continue

            # Both lanes free
            m_pal = palette_fn(m)
            if num_taps >= 2 and is_downbeat and not is_verse and (m % 2 == 0):
                if is_boss_proj:
                    md_timeline[m][f]['13'] = m_pal['BOSS_P1_G'] if is_proj1 else m_pal['BOSS_P2_G']
                    md_timeline[m][f]['14'] = m_pal['BOSS_P1_A'] if is_proj1 else m_pal['BOSS_P2_A']
                else:
                    if ground_count <= air_count:
                        md_timeline[m][f]['13'] = m_pal['HAMMER_G']
                        md_timeline[m][f]['14'] = m_pal['B1_A'] if b_toggle % 2 == 0 else m_pal['B2_A']
                    else:
                        md_timeline[m][f]['13'] = m_pal['B1_G'] if b_toggle % 2 == 0 else m_pal['B2_G']
                        md_timeline[m][f]['14'] = m_pal['HAMMER_A']
                ground_count += 1
                air_count += 1
                sim_count += 1
                lane_run = 0
            else:
                # Single Tap with PHRASE CLUSTERING
                diff = ground_count - air_count
                
                if diff >= 4:
                    chosen_lane = '14'
                elif diff <= -4:
                    chosen_lane = '13'
                else:
                    preferred = motif_base if f < 0.5 else motif_alt
                    if preferred == last_lane and lane_run >= 3:
                        chosen_lane = '14' if last_lane == '13' else '13'
                    elif preferred != last_lane and lane_run < 2 and (diff > -2 and diff < 2):
                        chosen_lane = last_lane
                    else:
                        chosen_lane = preferred

                if chosen_lane == last_lane:
                    lane_run += 1
                else:
                    lane_run = 1

                tok = pick_diverse_object(chosen_lane, m, f, is_boss_proj, is_proj1)
                md_timeline[m][f][chosen_lane] = tok
                if chosen_lane == '13': ground_count += 1
                else: air_count += 1
                last_lane = chosen_lane

            last_tap_pos = pos

    # Write BMS file (UTF-8)
    out_lines = []
    out_lines.append("*---------------------- HEADER FIELD")
    out_lines.append("")
    out_lines.append("#PLAYER 1")
    out_lines.append("#GENRE pop")
    out_lines.append(f"#TITLE {title}")
    out_lines.append(f"#ARTIST {artist}")
    out_lines.append(f"#BPM {bpm}")
    out_lines.append("#PLAYLEVEL 7")
    out_lines.append("#RANK 3")
    out_lines.append("")
    out_lines.append("#LNTYPE 1")
    out_lines.append("")
    out_lines.extend(wav_lines)
    out_lines.append("")
    out_lines.append("*---------------------- EXPANSION FIELD")
    out_lines.append("#BMSEDITER_OFFSET 0")
    out_lines.append("")
    out_lines.append("*---------------------- MAIN DATA FIELD")
    out_lines.append("")

    for m in range(max_meas + 1):
        for chan in ['13', '14', '15', '18']:
            chan_notes = [(f, md_timeline[m][f][chan]) for f in sorted(md_timeline[m].keys()) if chan in md_timeline[m][f]]
            if not chan_notes: continue

            best_slots = 16
            for d in [4, 8, 12, 16, 24, 32, 48, 64]:
                if all(abs(f * d - round(f * d)) < 1e-4 for f, _ in chan_notes):
                    best_slots = d
                    break
            
            slots = ['000'] * best_slots
            for f, tok in chan_notes:
                idx = int(round(f * best_slots))
                if idx < best_slots:
                    slots[idx] = tok
            
            out_lines.append(f"#{m:03d}{chan}:{''.join(slots)}")
        out_lines.append("")

    with open(out_path, 'w', encoding='utf-8') as f:
        f.write("\n".join(out_lines))

    total = ground_count + air_count
    ratio_13 = (ground_count / total * 100) if total else 0
    sim_ratio = (sim_count * 2 / total * 100) if total else 0
    print(f"[{title}] Downmix Master Complete!")
    print(f"   Total Notes: {total} (G: {ground_count} [{ratio_13:.1f}%], A: {air_count} [{100-ratio_13:.1f}%]) | Sim: {sim_count} ({sim_ratio:.1f}%)")

if __name__ == "__main__":
    def theme_white(m):
        if m < 18: return 0
        elif m < 34: return 1
        elif m < 50: return 2
        elif m < 86: return 0
        elif m < 102: return 1
        elif m < 115: return 2
        else: return 0

    def theme_miso(m):
        if m < 14: return 0
        elif m < 25: return 1
        elif m < 46: return 2
        else: return 0

    build_downmix(
        sixtar_path=r"H:\Sixtar Gate STARTRAIL custom mode\hwa\하얀 그리움\hwa2.bms",
        out_path=r"H:\muse dash hwa\hwa\하얀 그리움\hwa2.bms",
        title="하얀 그리움",
        artist="화영왕",
        bpm=128,
        palette_fn=lambda m: SCENE_2 if m >= 115 else SCENE_6,
        boss_plan=(50, 53, 54, 62, 66, 69, 70, 78, 83, 85),
        verse_ranges=[(0, 49), (86, 110)],
        theme_fn=theme_white
    )

    build_downmix(
        sixtar_path=r"H:\Sixtar Gate STARTRAIL custom mode\hwa\미소의 세상 너에게만\hwa2.bms",
        out_path=r"H:\muse dash hwa\hwa\미소의 세상 너에게만\hwa2.bms",
        title="미소의 세상 너에게만",
        artist="화영왕",
        bpm=124,
        palette_fn=lambda m: SCENE_4,
        boss_plan=(25, 28, 29, 33, 36, 38, 39, 41, 43, 45),
        verse_ranges=[(0, 24), (46, 60)],
        theme_fn=theme_miso
    )
