# Procedural World geological backtest

Generated 700 worlds in 21,2s. Scores validate topology and geological distributions; they are not artistic ratings.

| Profile | Resolution | Avg | Worst | Flat | Steep | Rivers | Reachable caves | Failures |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Natural | 65 | 99,6 | 94,0 | 19,0% | 17,5% | 1,82% | 100,0% | 10 / 50 |
| Alpine | 65 | 98,5 | 86,6 | 14,8% | 47,1% | 1,64% | 100,0% | 18 / 50 |
| Canyonlands | 65 | 99,8 | 96,6 | 20,5% | 25,3% | 1,81% | 100,0% | 5 / 50 |
| Karst | 65 | 99,3 | 90,0 | 29,6% | 2,9% | 2,00% | 100,0% | 11 / 50 |
| RollingPlains | 65 | 99,5 | 91,2 | 53,7% | 0,0% | 1,92% | 100,0% | 11 / 50 |
| Glacial | 65 | 99,6 | 79,6 | 21,2% | 35,3% | 1,84% | 100,0% | 3 / 50 |
| Volcanic | 65 | 100,0 | 100,0 | 22,9% | 17,8% | 1,89% | 100,0% | 1 / 50 |
| Natural | 129 | 100,0 | 98,7 | 22,7% | 11,3% | 2,37% | 100,0% | 2 / 50 |
| Alpine | 129 | 99,7 | 95,7 | 17,9% | 36,4% | 2,20% | 100,0% | 7 / 50 |
| Canyonlands | 129 | 100,0 | 100,0 | 22,6% | 25,7% | 2,28% | 100,0% | 0 / 50 |
| Karst | 129 | 99,9 | 98,3 | 30,8% | 2,6% | 2,47% | 100,0% | 6 / 50 |
| RollingPlains | 129 | 100,0 | 100,0 | 55,3% | 0,0% | 2,47% | 100,0% | 0 / 50 |
| Glacial | 129 | 100,0 | 99,9 | 23,9% | 28,7% | 2,15% | 100,0% | 1 / 50 |
| Volcanic | 129 | 100,0 | 100,0 | 27,6% | 7,9% | 2,37% | 100,0% | 0 / 50 |

**Best:** Alpine, seed `-392661752437002823`, score 100,0.
**Worst:** Glacial, seed `7924047624999685062`, score 79,6.
**3D render candidate:** Alpine, seed `-392661752437002823`, score 100,0, relief 785,3.

## Findings

- um bioma domina 73,8% do mapa: 3 cases
- ápice isolado excessivo (6,9): 3 cases
- ápice isolado excessivo (7,0): 3 cases
- inclinações saturadas em um mesmo valor (3,7%): 3 cases
- um bioma domina 74,7% do mapa: 3 cases
- faltam áreas planas (6,8%): 2 cases
- um bioma domina 76,7% do mapa: 2 cases
- um bioma domina 75,1% do mapa: 2 cases
- degrau máximo excessivo (17,1): 2 cases
- um bioma domina 72,6% do mapa: 2 cases
- um bioma domina 77,1% do mapa: 2 cases
- um bioma domina 76,6% do mapa: 2 cases
- um bioma domina 74,5% do mapa: 2 cases
- um bioma domina 74,4% do mapa: 2 cases
- degrau máximo excessivo (16,1): 2 cases
- um bioma domina 72,1% do mapa: 2 cases
- um bioma domina 73,0% do mapa: 1 cases
- faltam áreas planas (6,1%): 1 cases
- inclinações saturadas em um mesmo valor (4,2%): 1 cases
- um bioma domina 80,6% do mapa: 1 cases

## Worst cases

- Glacial `7924047624999685062` — 79,6: um bioma domina 72,7% do mapa; 2 componentes subterrâneos desconectados
- Alpine `-3719345503411677977` — 86,6: degrau máximo excessivo (24,4); um bioma domina 81,2% do mapa
- Karst `-3719345503411677977` — 90,0: um bioma domina 90,2% do mapa
- Alpine `7681369315911520508` — 90,8: ápice isolado excessivo (7,5); um bioma domina 87,1% do mapa
- RollingPlains `-8466692820785526154` — 91,2: um bioma domina 88,0% do mapa
- Alpine `4990025626462012731` — 93,6: ápice isolado excessivo (7,0); um bioma domina 83,2% do mapa
- Natural `-2448665380361343223` — 94,0: inclinações saturadas em um mesmo valor (4,2%); um bioma domina 80,6% do mapa
- Alpine `-7438691006823355954` — 94,4: um bioma domina 82,2% do mapa
- RollingPlains `-5775349131336018377` — 94,4: um bioma domina 82,1% do mapa
- Alpine `-5775349131336018377` — 94,9: degrau máximo excessivo (21,1)
