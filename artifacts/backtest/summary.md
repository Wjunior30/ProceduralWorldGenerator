# Procedural World backtest

Generated 70 worlds in 18,0s. Scores are diagnostic indicators, not artistic ratings.

| Profile | Avg score | Worst | Avg relief | Avg river | Avg cliffs | Failures |
|---|---:|---:|---:|---:|---:|---:|
| Natural | 86,8 | 75,4 | 666,9 | 1,85% | 27,2% | 9 / 10 |
| Alpine | 78,4 | 67,6 | 868,3 | 1,81% | 35,4% | 10 / 10 |
| Canyonlands | 90,9 | 82,1 | 547,1 | 1,85% | 20,4% | 8 / 10 |
| Karst | 88,3 | 77,8 | 597,8 | 1,83% | 22,4% | 9 / 10 |
| RollingPlains | 100,0 | 100,0 | 261,9 | 1,78% | 1,6% | 0 / 10 |
| Glacial | 79,7 | 70,0 | 771,0 | 1,75% | 28,9% | 9 / 10 |
| Volcanic | 87,0 | 77,9 | 767,4 | 1,96% | 33,3% | 9 / 10 |

**Best:** Glacial, seed `2026`, score 100,0.  
**Worst:** Alpine, seed `173133397`, score 67,6.  
**3D render candidate:** Glacial, seed `2026`, score 100,0, relief 646,7.

## Most frequent findings

- Abrupt slopes: 97 cases
- Biome diversity or dominance: 10 cases
- Boundary discontinuity: 3 cases

## Worst cases

- Alpine `173133397` — 67,6: degrau máximo excessivo (117,9); excesso de encostas abruptas (38,2%); um bioma domina 77,3% do mapa
- Alpine `4096` — 68,4: degrau máximo excessivo (106,4); excesso de encostas abruptas (34,7%); um bioma domina 81,1% do mapa
- Alpine `42` — 70,0: degrau máximo excessivo (125,1); excesso de encostas abruptas (45,6%)
- Glacial `42` — 70,0: degrau máximo excessivo (127,4); excesso de encostas abruptas (36,0%)
- Glacial `173133397` — 70,0: degrau máximo excessivo (119,9); excesso de encostas abruptas (31,0%)
- Glacial `4096` — 70,2: degrau máximo excessivo (108,7); excesso de encostas abruptas (29,9%); um bioma domina 76,7% do mapa; bordas têm variação média alta (18,2)
- Glacial `1` — 73,0: degrau máximo excessivo (102,8); excesso de encostas abruptas (35,7%); bordas têm variação média alta (23,0)
- Alpine `7` — 73,2: degrau máximo excessivo (107,2); excesso de encostas abruptas (51,0%)
- Alpine `1` — 74,0: degrau máximo excessivo (99,4); excesso de encostas abruptas (42,9%); bordas têm variação média alta (22,6)
- Natural `173133397` — 75,4: degrau máximo excessivo (98,4); excesso de encostas abruptas (34,7%)
