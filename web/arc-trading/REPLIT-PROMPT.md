# Промты для Replit

Два варианта. **Вариант А** — если заливаешь готовый `index.html` (быстрее и
предсказуемее). **Вариант Б** — если хочешь, чтобы Replit собрал всё с нуля.

---

# ВАРИАНТ А — заливаю готовый файл (рекомендую)

> Загрузи в Repl файл `index.html`, потом дай агенту промт ниже.

```
У меня в корне репла лежит index.html — готовая главная страница сайта
arc-trading.com (B2B-трейдер металлов и промышленной химии). Это статическая
страница без сборки: весь CSS и JS внутри файла. Задача — поднять её на
Replit и довести до продакшена.

СДЕЛАЙ:

1. Подними статический сервер на Express:
   - файл server.js, слушает process.env.PORT || 5000, host '0.0.0.0'
   - express.static на папку public
   - fallback на public/index.html
   - перенеси index.html в public/index.html
   - package.json со скриптом "start": "node server.js"
   Проверь, что страница открывается в webview.

2. Скачай все картинки локально, сейчас они хотлинкаются с боевого сайта.
   Положи в public/img/ и замени в index.html все вхождения
   https://arc-trading.com/img/ на img/
   Список файлов:
   head.jpg, about1.jpg, about2.jpg, about3.jpg, logo.png,
   u1.jpg u2.jpg u3.jpg u4.jpg u5.jpg u6.jpg u7.jpg u8.jpg u9.jpg u10.jpg
   u11.jpg u12.jpg u13.jpg u14.jpg u15.jpg u16.jpg u17.jpg u18.jpg u19.jpg
   u20.jpg u21.jpg u22.jpg u23.jpg u24.jpg u25.jpg
   Все берутся с https://arc-trading.com/img/<имя>

3. Пережми картинки в WebP (sharp), отдавай <picture> с fallback на jpg.
   Ширины: hero — 1920, карточки товаров — 800, about — 800.

4. Сделай форму обратной связи рабочей:
   - POST /api/contact на Express
   - валидация name / email / message на сервере
   - отправка на office@arc-trading.com через nodemailer, SMTP-креды из
     Replit Secrets (SMTP_HOST, SMTP_PORT, SMTP_USER, SMTP_PASS)
   - фронт уже готов: в index.html найди обработчик submit у #form, там
     стоит заглушка на setTimeout — замени на fetch('/api/contact')
   - оставь текущие состояния кнопки: loading → галочка → тост
   - если SMTP-секреты не заданы, пиши заявку в data/leads.json и отвечай 200

5. Ничего не меняй в текстах, названиях товаров и цифрах — они перенесены
   с боевого сайта дословно и менять их нельзя.

6. SEO не делай вообще: никаких мета-тегов, sitemap, robots.txt,
   schema.org, Open Graph. Не трать на это время.

Когда закончишь — покажи превью и скажи, какие секреты нужно добавить.
```

---

# ВАРИАНТ Б — собрать с нуля

```
Собери одностраничный сайт для ARC Trading — турецкого B2B-трейдера чёрных и
цветных металлов и промышленной химии (Стамбул). Это редизайн существующего
устаревшего сайта arc-trading.com: структура и тексты остаются, меняется
только оболочка.

СТЕК
Статика без фреймворков: один index.html со встроенными CSS и JS, плюс
Express-сервер (server.js, порт process.env.PORT || 5000, host '0.0.0.0',
express.static на public). Никакого React, Tailwind, сборщиков. Из внешнего
только: шрифты Google (Inter Tight + Inter), GSAP + ScrollTrigger и Lenis с
CDN.

SEO НЕ ДЕЛАЙ. Никаких мета-тегов, sitemap, robots, schema.org, Open Graph,
alt-текстов под ключи. Не трать на это токены.

ПАЛИТРА — СВЕТЛАЯ
  --bg        #FFFFFF    основной фон
  --bg-alt    #F6F7F9    чередующиеся секции
  --bg-deep   #EDEFF2    плейсхолдеры
  --ink       #0E1116    основной текст
  --ink-2     #5A6472    вторичный текст
  --amber     #F5A623    акцент (из логотипа), --amber-2 #E8930C
  --graphite  #141922    ТОЛЬКО футер и тёмные кнопки
  --line      rgba(14,17,22,.08)
  тени        0 10px 40px rgba(14,17,22,.06) и 0 26px 70px rgba(14,17,22,.12)
  радиусы 18px, макс. ширина 1440px, боковые поля clamp(16px,4vw,72px)

ТИПОГРАФИКА
Заголовки Inter Tight 700–800, letter-spacing -.03em, h1 до clamp(46px,8.4vw,120px).
Надзаголовки-эйбрау: 12px, UPPERCASE, letter-spacing .2em, серые, с короткой
янтарной чертой слева. Текст Inter 17px / line-height 1.7. Цифры tabular-nums.

СТРУКТУРА (строго в этом порядке)
Шапка → Hero → About us → Our products → Key advantages → Contact → футер.

--- ШАПКА
Логотип ARC (SVG-обводка, перекладина буквы A янтарная) + слово TRADING
разрядкой. Меню: Home / About us / Our products / Key advantages / Contact.
Переключатель EN|TR (TR ведёт на /tr/). Кнопка «Contact us».
Прозрачная над героем, при скролле — белая стеклянная с backdrop-blur и
тенью, высота с 86px на 70px. Подчёркивание пункта — янтарная линия, растущая
из центра. Ниже 1080px — бургер и полноэкранное меню с круговым clip-path и
вылетом пунктов со стаггером.

--- HERO, на весь экран
Фон — фото карьера, десатурировано, сверху светлый градиент
linear-gradient(120deg, rgba(255,255,255,.95), rgba(255,255,255,.5)) плюс
подложка снизу. Слабая анимированная сетка 96×96 с radial-маской.
Эйбрау: «Metals & Chemicals · Istanbul»
H1: «ARC Trading Company», слово ARC янтарное
Подзаголовок: «Your Global Partner in Metals and Chemicals Trading»
Кнопки: «Contact us» (тёмная) и «Our products» (контурная)
Справа вертикальная надпись Scroll с пульсирующей линией.
Внизу во всю ширину — бегущая строка со всеми 25 названиями товаров через
янтарные точки, на ховер останавливается.

--- ABOUT US, две колонки
Слева эйбрау «About», заголовок «About us», два абзаца и кнопка «Contacts».
Тексты дословно:
«Arc Trading (Arc Trading İç ve Dış Ticaret Limited Şirketi) is a trusted metal
trader and strategic partner of metallurgical enterprises worldwide.
Specializing in high-quality ferrous and non-ferrous metals, we serve
industries across the CIS, Europe, and beyond.»
«Our extensive global network ensures reliable, on-time deliveries. Committed
to innovation and transparency, we offer customized, scalable solutions to
drive business growth. Whether in metals or chemicals, Arc Trading empowers
industries to succeed in a competitive market.»
Справа коллаж из трёх фото с разной скоростью параллакса, на нём тёмная
плашка со счётчиком «25 / PRODUCT POSITIONS».
Слева от секции тонкая янтарная вертикальная линия, прорисовывающаяся при
скролле.

--- OUR PRODUCTS — ВАЖНО: на главной только 6 карточек
Эйбрау «Catalogue», заголовок «Our products», справа янтарная кнопка
«View all 25 products» → /service/.
Два абзаца дословно:
«Arc Trading supplies high-quality ferrous and non-ferrous metals, including
rebar, wire rod, and various metal structures, as well as essential industrial
chemicals.»
«We ensure reliable deliveries tailored to diverse industries worldwide.
Explore our full product range below to find the right solutions for your
business.»
Сетка 3 колонки × 2 ряда = 6 карточек. Не выводи все 25 на главную.
Показываем: REBAR (u1), WIRE ROD (u2), HRS PLATE (u3), PIG IRON (u10),
FERROSILICON (u13), COKE PRODUCTS (u15).
Карточка: фото 16/11 сверху, в углу фото — плашка с группой
(Long products / Semi-finished / Ferroalloys / Chemicals), ниже название
капсом и ссылка «More details» → /service/#u<номер>.
Ховер: подъём карточки, 3D-tilt по мыши, zoom фото, возврат насыщенности,
выезжающая снизу светлая подложка под текстом, янтарная стрелка.
Под сеткой строка-разделитель: слева текст «Rebar, wire rod, structures,
semi-finished products, ferroalloys, chemicals and industrial gases — 25
positions in total.», справа контурная кнопка «Full product range» → /service/.
Массив всех 25 товаров держи в JS, витрину задавай списком FEATURED — чтобы
позиции на главной менялись одной строкой.

--- KEY ADVANTAGES
Эйбрау «Why Arc Trading», заголовок «Key advantages».
Блок «TOTAL PRODUCTION CAPACITY» — три карточки с крупными счётчиками,
анимация count-up при попадании в вьюпорт, десятичная запятая:
  4,7 m. mt/y. → подпись «Pigiron – 4,7 m. mt/y.»
  5,0 m. mt/y. → подпись «Steel – 5,0 m.mt/y.»
  3,9 m. mt/y. → подпись «Coke – 3,9 m. mt/y.»
Ниже пять нумерованных строк с гигантскими полупрозрачными цифрами 01–05,
на ховер строка подсвечивается белой подложкой, цифра желтеет, стрелка
уезжает вправо. Тексты дословно:
01 TOTAL PRODUCTION CAPACITY — Pigiron – 4,7 m. mt/y. · Steel – 5,0 m.mt/y. ·
   Coke – 3,9 m. mt/y.
02 RELIABILITY — All products comply with GOST and international standards of
   technical and physical indicators
03 A WIDE RANGE OF PRODUCTS — The company offers its customers an extensive
   product line: from cast iron and semi-finished products to long, shaped and
   flat rolled products in a wide range of steel grades
04 FLEXIBLE FINANCIAL CONDITIONS AND COMPETITIVE PRICING — The total number of
   employees of the enterprises included in the partner network is 20 000
   persons.  (20 000 — счётчик, разделитель пробел)
05 LOGISTICS SERVICE — Our ability to deliver products anywhere in the world
   on any delivery terms. We delivered ~600.000 mt metallurgical products in
   2023.  (600.000 — счётчик, разделитель точка)

--- CONTACT, две колонки, фон #F6F7F9
Слева эйбрау «Get in touch», заголовок «Contact us», текст дословно:
«We are open to cooperation, if you have an offer for us, you can send it
through the form below. Our specialist will contact you shortly»
Форма: Your Name / E-mail / Message — floating-label, янтарная линия фокуса,
растущая из левого края. Кнопка: loading-спиннер → рисующаяся SVG-галочка →
тост «Thank you — our specialist will contact you shortly».
Справа три строки контактов, каждая с иконкой и копированием в буфер по клику
(тост «Copied»):
  Phone   +90(212)2147460
  E-mail  office@arc-trading.com
  Office  Ayazağa Mahallesi, Kemerburgaz Caddesi, Vadistanbul 7B Ofis Blok,16.
          Kat/ no 62, Sarıyer/İSTANBUL - 34396
Под ними светлая карта-схема (SVG) с пульсирующим янтарным маркером и
плашкой «Vadistanbul 7B · Sarıyer / İSTANBUL».

--- ФУТЕР, единственная тёмная зона (#141922)
Три колонки:
  1) «Arc Trading (Arc Trading İç ve Dış Ticaret Limited Şirketi) is a trusted
     metal trader and strategic partner of metallurgical enterprises from CIS
     countries and Europe»
  2) NAVIGATION — Home / About us / Our products / Key advantages / Contact
  3) CONTACT — телефон, адрес, почта
Под ними надпись ARC TRADING обводкой во всю ширину (SVG text, textLength на
100%), на ховер заливается янтарным.
Низ: «© 2024 | All right reserved.» и «Arc Trading İç ve Dış Ticaret Limited
Şirketi».

ЭФФЕКТЫ — накидай щедро
  · прелоадер: логотип ARC рисуется SVG-штрихом, снизу полоса загрузки
  · шторка-переход между страницами (графитовая, scaleY)
  · прогресс-бар чтения вверху, янтарный градиент
  · кастомный курсор: точка + догоняющее кольцо, на интерактиве кольцо
    раздувается и желтеет; на тач-устройствах выключен
  · magnetic-кнопки (смещение к курсору)
  · smooth scroll через Lenis
  · scroll-reveal всех секций: fade + translateY(40px), стаггер 60–90ms
  · параллакс фона героя и коллажа в About
  · посимвольная анимация H1 (слова оборачивай в inline-block, чтобы не
    рвались при переносе)
  · count-up счётчики
  · 3D-tilt карточек и блоков мощностей
  · back-to-top и тосты
  · всё глушится через @media (prefers-reduced-motion: reduce)

ТРЕБОВАНИЯ
  · mobile-first, проверь 390 / 768 / 1440 / 1920 — горизонтального скролла
    быть не должно нигде
  · тач-таргеты от 44px
  · анимации только на transform/opacity, через IntersectionObserver и rAF
  · lazy-load всех картинок кроме героя, у героя fetchpriority="high"
  · контраст текста не ниже AA — не уходи в бледно-серый на белом

КАРТИНКИ
Скачай с боевого сайта в public/img/ и пережми в WebP:
https://arc-trading.com/img/head.jpg, about1.jpg, about2.jpg, about3.jpg,
u1.jpg … u25.jpg (u9 и u21 тоже есть).
Соответствие: u1 REBAR, u2 WIRE ROD, u3 HRS PLATE, u4 STRUCTURAL CHANNEL,
u5 STEEL BEAM, u6 STEEL ANGLE, u7 STEEL RODS, u8 STEEL MINE STAND, u9 бульб,
u10 PIG IRON, u11 STEEL SLABS, u12 SQUARE STEEL BILLETS, u13 FERROSILICON,
u14 FERROSILICON MANGANESE, u15 COKE PRODUCTS, u16 AMMONIUM SULFATE,
u17 IRON ORE FINES, u18 COAL TAR, u19 CRUDE COAL BENZENE, u20 FLUXIC
LIMESTONE, u21 газы, u22 GRANULATED SLAG, u23 STEELMAKING CRUSHED STONE,
u24 QUICKLIME LUMPY, u25 CONVERTER SLAG.
ВНИМАНИЕ: u3 и u13 идут с водяными знаками Shutterstock — подбери им замену
или оставь заглушку и скажи мне, какие файлы надо заменить.

ЧЕГО НЕ ДЕЛАТЬ
  · не менять ни слова в текстах, названиях товаров и цифрах
  · не выводить все 25 товаров на главную — только 6
  · не делать тёмную тему, сайт светлый
  · не заниматься SEO
  · не добавлять секции, которых нет в списке выше
```

---

## Полный список товаров (для страницы /service/)

Порядок и написание — как на боевом сайте, менять нельзя.

| # | Название | Картинка | Группа |
|---|---|---|---|
| 1 | REBAR | u1 | Long products |
| 2 | WIRE ROD | u2 | Long products |
| 3 | HRS PLATE | u3 | Long products |
| 4 | STRUCTURAL CHANNEL | u4 | Long products |
| 5 | STEEL BEAM | u5 | Long products |
| 6 | STEEL ANGLE | u6 | Long products |
| 7 | STEEL RODS | u7 | Long products |
| 8 | STEEL MINE STAND | u8 | Long products |
| 9 | PIG IRON | u10 | Semi-finished |
| 10 | STEEL SLABS | u11 | Semi-finished |
| 11 | SQUARE STEEL BILLETS | u12 | Semi-finished |
| 12 | FERROSILICON | u13 | Ferroalloys |
| 13 | FERROSILICON MANGANESE | u14 | Ferroalloys |
| 14 | COKE PRODUCTS | u15 | Chemicals |
| 15 | AMMONIUM SULFATE | u16 | Chemicals |
| 16 | IRON ORE FINES | u17 | Raw materials & slag |
| 17 | COAL TAR | u18 | Chemicals |
| 18 | CRUDE COAL BENZENE | u19 | Chemicals |
| 19 | FLUXIC LIMESTONE \| RUBBLE | u20 | Raw materials & slag |
| 20 | GRANULATED SLAG \| DUMP SLAG | u22 | Raw materials & slag |
| 21 | STEELMAKING CRUSHED STONE | u23 | Raw materials & slag |
| 22 | QUICKLIME LUMPY | u24 | Raw materials & slag |
| 23 | CONVERTER SLAG | u25 | Raw materials & slag |
| 24 | Hot-rolled steel unsymmetrical bulb section for shipbuilding | u9 | Long products |
| 25 | ARGON LIQUID/GASEOUS \| OXYGEN LIQUID/GASEOUS NITROGEN LIQUID \| KRYPTONOXENONE MIXTURE \| NEON-HELIUM MIXTURE | u21 | Industrial gases |

На боевом сайте в позиции 25 опечатка: `KRYPTONOXENONE MIXTURE` вместо
KRYPTON-XENON. Перенесено дословно — исправлять только по твоему решению.
