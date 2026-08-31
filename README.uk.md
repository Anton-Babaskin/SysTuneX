<div align="center">

<img src="src/SysTuneX.App/Assets/SysTuneX.png" width="112" alt="Логотип ігрового оптимізатора Windows SysTuneX">

# SysTuneX

### Ігровий оптимізатор Windows 10/11 і налаштовуваний монітор FPS

**Вичави максимум із Windows. Контролюй кожен кадр. Точно відновлюй зміни.**

Безкоштовний Windows-оптимізатор із відкритим кодом для геймерів: профілі продуктивності, ігровий режим і моніторинг **FPS, 1% low, frame time, CPU, GPU, RAM та температур**.

[English](README.md) · [Русский](README.ru.md) · [Українська](README.uk.md) · [Español](README.es.md)

[⬇️ **Завантажити SysTuneX**](https://github.com/Anton-Babaskin/SysTuneX/releases/latest/download/SysTuneX.exe) · [Останній реліз](https://github.com/Anton-Babaskin/SysTuneX/releases/latest) · [SHA-256](https://github.com/Anton-Babaskin/SysTuneX/releases/latest/download/SHA256SUMS.txt) · [Повідомити про помилку](https://github.com/Anton-Babaskin/SysTuneX/issues)

[![Build](https://github.com/Anton-Babaskin/SysTuneX/actions/workflows/build.yml/badge.svg)](https://github.com/Anton-Babaskin/SysTuneX/actions/workflows/build.yml)
[![Release](https://img.shields.io/github/v/release/Anton-Babaskin/SysTuneX?include_prereleases\&sort=semver)](https://github.com/Anton-Babaskin/SysTuneX/releases)
[![License](https://img.shields.io/github/license/Anton-Babaskin/SysTuneX)](LICENSE)
![Windows 10/11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?logo=windows11\&logoColor=white)

</div>

![Монітор FPS, CPU, GPU, RAM і температур SysTuneX](docs/images/systunex-monitor-ru.png)

## Що отримує геймер

- 🎮 Профілі для competitive FPS, battle royale, open world, перегонів, стримінгу та максимальної продуктивності.
- 📈 Вибір показників монітора: FPS, 1% low, frame time, навантаження й температура CPU/GPU, RAM, вентилятор і процеси.
- ⚡ Тимчасовий Game Mode із відновленням служб і попередньої схеми живлення після гри.
- ↩️ Журнал змін і точне повернення фактичного попереднього стану.
- 🛡️ Підрахунок кадрів через Windows ETW без ін'єкції коду в гру.
- 🧹 Очищення, мережеві налаштування, приватність, служби та діагностика в одному застосунку.

## Безпека та чесні обмеження

SysTuneX спочатку записує вихідний стан підтримуваного параметра і лише потім змінює його. Розширені налаштування показують наслідки та потребують підтвердження. Програма не обіцяє однаковий приріст FPS на кожному комп'ютері. Монітор є налаштовуваною панеллю для другого екрана, а не внутрішньоігровим overlay.

## Системні вимоги

- Windows 10 1809 / build 17763 або новіша
- Windows 11
- x64
- Права адміністратора
- Встановлення .NET не потрібне

## Початок роботи

1. Завантажте актуальний `SysTuneX.exe`.
2. За бажанням перевірте файл за `SHA256SUMS.txt`.
3. Запустіть від імені адміністратора.
4. Перегляньте описи та рівні ризику перед застосуванням змін.

> Виконуваний файл наразі не підписаний цифровим підписом, тому Windows SmartScreen може показати попередження.

Повна технічна документація, архітектура та інструкції зі збірки доступні в [англійському README](README.md). Проєкт поширюється за ліцензією [MIT](LICENSE).
