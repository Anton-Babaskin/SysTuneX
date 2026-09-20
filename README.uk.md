<div align="center">

<img src="src/SysTuneX.App/Assets/SysTuneX.png" width="112" alt="Логотип ігрового оптимізатора Windows SysTuneX">

# SysTuneX

### Ігровий оптимізатор Windows 10/11 і налаштовуваний монітор FPS

**Вичави максимум із Windows. Контролюй кожен кадр. Точно відновлюй зміни.**

Безкоштовний Windows-оптимізатор із відкритим кодом для геймерів: профілі продуктивності, ігровий режим і моніторинг **FPS, 1% low, frame time, CPU, GPU, RAM та температур**.

[![English](https://img.shields.io/badge/English-5A6570)](README.md) [![Русский](https://img.shields.io/badge/%D0%A0%D1%83%D1%81%D1%81%D0%BA%D0%B8%D0%B9-5A6570)](README.ru.md) [![Українська](https://img.shields.io/badge/%D0%A3%D0%BA%D1%80%D0%B0%D1%97%D0%BD%D1%81%D1%8C%D0%BA%D0%B0-0078D4)](README.uk.md) [![Español](https://img.shields.io/badge/Espa%C3%B1ol-5A6570)](README.es.md)

[🌐 **Сайт**](https://anton-babaskin.github.io/SysTuneX/) · [⬇️ **Завантажити SysTuneX**](https://github.com/Anton-Babaskin/SysTuneX/releases/latest/download/SysTuneX.exe) · [Останній реліз](https://github.com/Anton-Babaskin/SysTuneX/releases/latest) · [SHA-256](https://github.com/Anton-Babaskin/SysTuneX/releases/latest/download/SHA256SUMS.txt) · [Повідомити про помилку](https://github.com/Anton-Babaskin/SysTuneX/issues)

[![Build](https://github.com/Anton-Babaskin/SysTuneX/actions/workflows/build.yml/badge.svg)](https://github.com/Anton-Babaskin/SysTuneX/actions/workflows/build.yml)
[![Release](https://img.shields.io/github/v/release/Anton-Babaskin/SysTuneX?include_prereleases\&sort=semver)](https://github.com/Anton-Babaskin/SysTuneX/releases)
[![License](https://img.shields.io/github/license/Anton-Babaskin/SysTuneX)](LICENSE)
![Windows 10/11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?logo=windows11\&logoColor=white)
![UI languages](https://img.shields.io/badge/UI-English%20%C2%B7%20%D0%A0%D1%83%D1%81%D1%81%D0%BA%D0%B8%D0%B9%20%C2%B7%20%D0%A3%D0%BA%D1%80%D0%B0%D1%97%D0%BD%D1%81%D1%8C%D0%BA%D0%B0-5C2D91)

</div>

![Монітор FPS, CPU, GPU, RAM і температур SysTuneX](docs/images/systunex-monitor-ru.png)

## Що отримує геймер

- 🎮 Профілі для competitive FPS, battle royale, open world, перегонів, стримінгу та максимальної продуктивності.
- 📈 Вибір показників монітора: FPS, 1% low, frame time, навантаження й температура CPU/GPU, RAM, вентилятор і процеси.
- 🪟 Компактна панель: ті самі цифри в маленькому вікні поверх інших, за **Ctrl+Shift+M** — над іграми у віконному режимі без рамки, не торкаючись процесу гри.
- ⚡ Тимчасовий Game Mode із відновленням служб і попередньої схеми живлення після гри.
- ↩️ Журнал змін і точне повернення фактичного попереднього стану.
- 🛡️ Підрахунок кадрів через Windows ETW без ін'єкції коду в гру.
- 🧹 Очищення, мережеві налаштування, приватність, служби та діагностика в одному застосунку.
- 🇺🇦 **Інтерфейс українською** — повністю, разом з описом кожного твіка, служби та профілю.

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
