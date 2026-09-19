/* ============================================================
   ARC Trading — shared behaviour for every page
   ============================================================ */
(function(){
  'use strict';
  var RM = matchMedia('(prefers-reduced-motion: reduce)').matches;
  var $  = function(s,c){return (c||document).querySelector(s);};
  var $$ = function(s,c){return Array.prototype.slice.call((c||document).querySelectorAll(s));};
  var ARROW = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M5 12h14M13 6l6 6-6 6"/></svg>';

  /* ============================================================
     catalogue — names, order and links kept as on the live site
     ============================================================ */
  var GROUPS = [
    ['all','All products'],
    ['long','Long products'],
    ['semi','Semi-finished'],
    ['ferro','Ferroalloys'],
    ['chem','Chemicals'],
    ['raw','Raw materials & slag'],
    ['gas','Industrial gases']
  ];
  var PRODUCTS = [
    ['REBAR','u1','long'],
    ['WIRE ROD','u2','long'],
    ['HRS PLATE','u3','long'],
    ['STRUCTURAL CHANNEL','u4','long'],
    ['STEEL BEAM','u5','long'],
    ['STEEL ANGLE','u6','long'],
    ['STEEL RODS','u7','long'],
    ['STEEL MINE STAND','u8','long'],
    ['PIG IRON','u10','semi'],
    ['STEEL SLABS','u11','semi'],
    ['SQUARE STEEL BILLETS','u12','semi'],
    ['FERROSILICON','u13','ferro'],
    ['FERROSILICON MANGANESE','u14','ferro'],
    ['COKE PRODUCTS','u15','chem'],
    ['AMMONIUM SULFATE','u16','chem'],
    ['IRON ORE FINES','u17','raw'],
    ['COAL TAR','u18','chem'],
    ['CRUDE COAL BENZENE','u19','chem'],
    ['FLUXIC LIMESTONE | RUBBLE','u20','raw'],
    ['GRANULATED SLAG | DUMP SLAG','u22','raw'],
    ['STEELMAKING CRUSHED STONE','u23','raw'],
    ['QUICKLIME LUMPY','u24','raw'],
    ['CONVERTER SLAG','u25','raw'],
    ['Hot-rolled steel unsymmetrical bulb section for shipbuilding','u9','long'],
    ['ARGON LIQUID/GASEOUS | OXYGEN LIQUID/GASEOUS NITROGEN LIQUID | KRYPTONOXENONE MIXTURE | NEON-HELIUM MIXTURE','u21','gas']
  ];
  /* the six positions shown on the home page */
  var FEATURED = ['u1','u2','u3','u10','u13','u15'];
  var IMG = 'https://arc-trading.com/img/';
  var GLABEL = {};
  GROUPS.forEach(function(g){ GLABEL[g[0]] = g[1]; });
  window.ARC = { PRODUCTS: PRODUCTS, GROUPS: GROUPS, IMG: IMG };

  function cardHTML(p){
    return '<div class="card__ph">' +
        '<span class="card__tag">' + GLABEL[p[2]] + '</span>' +
        '<img src="' + IMG + p[1] + '.jpg" alt="" loading="lazy">' +
      '</div>' +
      '<div class="card__body">' +
        '<h3>' + p[0] + '</h3>' +
        '<span class="card__more">More details ' + ARROW + '</span>' +
      '</div>' +
      '<a class="card__link" href="products.html#' + p[1] + '" aria-label="' + p[0] + ' — More details"></a>';
  }
  function renderCards(grid, list, perRow){
    list.forEach(function(p, i){
      var el = document.createElement('article');
      el.className = 'card';
      el.id = p[1];
      el.dataset.g = p[2];
      el.dataset.tilt = '';
      el.style.transitionDelay = (i % perRow) * 90 + 'ms';
      el.setAttribute('data-rv','');
      el.innerHTML = cardHTML(p);
      grid.appendChild(el);
    });
  }

  var grid = $('#grid');
  if (grid){
    if (grid.dataset.mode === 'all'){
      renderCards(grid, PRODUCTS, 4);
    } else {
      renderCards(grid, PRODUCTS.filter(function(p){ return FEATURED.indexOf(p[1]) > -1; }), 3);
    }
  }

  /* ---- chips + FLIP, catalogue page only ---- */
  var chips = $('#chips');
  if (chips && grid){
    GROUPS.forEach(function(g){
      var n = g[0] === 'all' ? PRODUCTS.length : PRODUCTS.filter(function(p){ return p[2] === g[0]; }).length;
      var b = document.createElement('button');
      b.className = 'chip' + (g[0] === 'all' ? ' on' : '');
      b.dataset.g = g[0];
      b.innerHTML = g[1] + '<em>' + n + '</em>';
      chips.appendChild(b);
    });
    chips.addEventListener('click', function(e){
      var chip = e.target.closest('.chip');
      if (!chip) return;
      $$('.chip', chips).forEach(function(c){ c.classList.toggle('on', c === chip); });
      var g = chip.dataset.g, cards = $$('.card', grid), first = {};
      cards.forEach(function(c,i){ first[i] = c.getBoundingClientRect(); });
      cards.forEach(function(c){ c.classList.toggle('hide', g !== 'all' && c.dataset.g !== g); });
      var live = $('#chipCount');
      if (live) live.textContent = cards.filter(function(c){ return !c.classList.contains('hide'); }).length;
      if (RM) return;
      cards.forEach(function(c,i){
        if (c.classList.contains('hide')) return;
        var last = c.getBoundingClientRect();
        var dx = first[i].left - last.left, dy = first[i].top - last.top;
        if (!dx && !dy) return;
        c.animate([{transform:'translate(' + dx + 'px,' + dy + 'px)'},{transform:'none'}],
                  {duration:520, easing:'cubic-bezier(.22,1,.36,1)'});
      });
    });
  }

  /* ---- marquee ---- */
  var mq = $('#marq');
  if (mq){
    var strip = PRODUCTS.map(function(p){ return '<span>' + p[0].split(' | ')[0] + '</span>'; }).join('');
    mq.innerHTML = strip + strip;
  }

  /* ============================================================
     headline split — words stay unbreakable
     ============================================================ */
  $$('[data-split]').forEach(function(h1){
    var idx = 0;
    (function split(node){
      Array.prototype.slice.call(node.childNodes).forEach(function(n){
        if (n.nodeType === 3){
          var frag = document.createDocumentFragment();
          n.textContent.split(/(\s+)/).forEach(function(tok){
            if (!tok) return;
            if (/^\s+$/.test(tok)){ frag.appendChild(document.createTextNode(' ')); return; }
            var word = document.createElement('span');
            word.className = 'wd';
            tok.split('').forEach(function(ch){
              var s = document.createElement('span');
              s.className = 'ch';
              s.textContent = ch;
              s.style.animationDelay = (0.28 + idx * 0.028) + 's';
              idx++;
              word.appendChild(s);
            });
            frag.appendChild(word);
          });
          node.replaceChild(frag, n);
        } else if (n.nodeType === 1){ split(n); }
      });
    })(h1);
  });

  /* ============================================================
     hero slider
     ============================================================ */
  var stage = $('#slides'), copies = $$('.hero__copy'), dotsBox = $('#dots');
  if (stage && copies.length > 1){
    var imgs = $$('.slide', stage), n = imgs.length, cur = 0, timer = null;
    var DUR = 7000;

    copies.forEach(function(c, i){
      var d = document.createElement('button');
      d.className = 'dot' + (i === 0 ? ' on' : '');
      d.style.setProperty('--dur', (DUR / 1000) + 's');
      d.setAttribute('aria-label', 'Slide ' + (i + 1));
      d.innerHTML = '<b></b><svg width="34" height="34" viewBox="0 0 34 34"><circle cx="17" cy="17" r="15.9"/></svg>';
      d.addEventListener('click', function(){ go(i, true); });
      dotsBox.appendChild(d);
    });
    var counter = document.createElement('span');
    counter.className = 'hero__count';
    counter.innerHTML = '<b>01</b> / ' + String(n).padStart(2,'0');
    dotsBox.appendChild(counter);
    var dots = $$('.dot', dotsBox);

    function go(i, manual){
      if (i === cur) return;
      imgs[cur].classList.remove('on');
      copies[cur].classList.remove('on');
      dots[cur].classList.remove('on');
      cur = (i + n) % n;
      imgs[cur].classList.add('on');
      copies[cur].classList.add('on');
      /* restart the ring animation */
      var d = dots[cur], c = $('circle', d);
      c.style.animation = 'none'; void c.offsetWidth; c.style.animation = '';
      d.classList.add('on');
      counter.innerHTML = '<b>' + String(cur + 1).padStart(2,'0') + '</b> / ' + String(n).padStart(2,'0');
      /* restart the Ken Burns pan */
      var im = $('img', imgs[cur]);
      im.style.animation = 'none'; void im.offsetWidth; im.style.animation = '';
      if (manual) start();
    }
    function next(){ go(cur + 1); }
    function start(){ stop(); if (!RM) timer = setInterval(next, DUR); }
    function stop(){ if (timer){ clearInterval(timer); timer = null; } }

    var hero = $('.hero');
    hero.addEventListener('mouseenter', stop);
    hero.addEventListener('mouseleave', start);
    document.addEventListener('visibilitychange', function(){ document.hidden ? stop() : start(); });
    addEventListener('keydown', function(e){
      if (e.key === 'ArrowRight'){ go(cur + 1, true); }
      if (e.key === 'ArrowLeft'){ go(cur - 1, true); }
    });
    var tx = 0;
    hero.addEventListener('touchstart', function(e){ tx = e.touches[0].clientX; stop(); }, {passive:true});
    hero.addEventListener('touchend', function(e){
      var dx = e.changedTouches[0].clientX - tx;
      if (Math.abs(dx) > 50) go(cur + (dx < 0 ? 1 : -1), true); else start();
    }, {passive:true});
    start();
  }

  /* ============================================================
     preloader / page transition
     ============================================================ */
  var curtain = $('#curtain');
  function boot(){
    document.documentElement.classList.add('loaded');
    setTimeout(function(){ var p = $('#preloader'); if (p) p.remove(); }, 700);
  }
  window.addEventListener('load', function(){ setTimeout(boot, RM ? 0 : 1000); });
  setTimeout(boot, 3200);

  document.addEventListener('click', function(e){
    var a = e.target.closest('a[href]');
    if (!a) return;
    var href = a.getAttribute('href');
    if (!href || href.charAt(0) === '#' || a.target === '_blank' ||
        href.indexOf('mailto:') === 0 || href.indexOf('tel:') === 0) return;
    if (location.protocol === 'file:') return;
    if (a.origin !== location.origin) return;
    e.preventDefault();
    if (curtain) curtain.classList.add('in');
    setTimeout(function(){ location.href = href; }, 460);
  });

  /* ============================================================
     smooth scroll
     ============================================================ */
  var lenis = null;
  function initSmooth(){
    if (RM || !window.Lenis){ document.documentElement.style.scrollBehavior = 'smooth'; return; }
    lenis = new window.Lenis({ duration: 1.1, smoothWheel: true });
    (function raf(t){ lenis.raf(t); requestAnimationFrame(raf); })();
    if (window.ScrollTrigger) lenis.on('scroll', window.ScrollTrigger.update);
  }
  document.addEventListener('click', function(e){
    var a = e.target.closest('a[href^="#"]');
    if (!a) return;
    var id = a.getAttribute('href');
    if (id === '#') return;
    var el = id === '#top' ? document.body : $(id);
    if (!el) return;
    e.preventDefault();
    closeMenu();
    if (lenis) lenis.scrollTo(el, { offset: id === '#top' ? 0 : -70 });
    else window.scrollTo({ top: id === '#top' ? 0 : el.getBoundingClientRect().top + pageYOffset - 70,
                           behavior: RM ? 'auto' : 'smooth' });
  });

  /* ============================================================
     header, progress, back-to-top
     ============================================================ */
  var hdr = $('#hdr'), prog = $('#progress'), totop = $('#totop');
  function onScroll(){
    var y = pageYOffset || document.documentElement.scrollTop;
    if (hdr){ hdr.classList.toggle('stuck', y > 40); hdr.classList.toggle('on-hero', y <= 40); }
    if (totop) totop.classList.toggle('on', y > 600);
    if (prog){
      var h = document.documentElement.scrollHeight - innerHeight;
      prog.style.transform = 'scaleX(' + (h > 0 ? Math.min(y / h, 1) : 0) + ')';
    }
  }
  addEventListener('scroll', onScroll, { passive: true });
  onScroll();

  /* ============================================================
     reveal + counters
     ============================================================ */
  var io = new IntersectionObserver(function(en){
    en.forEach(function(e){
      if (!e.isIntersecting) return;
      var t = e.target;
      t.classList.add('in');
      io.unobserve(t);
      if (t.style.transitionDelay) setTimeout(function(){ t.style.transitionDelay = ''; }, 1200);
    });
  }, { threshold: 0.12, rootMargin: '0px 0px -8% 0px' });
  $$('[data-rv],[data-rv-x],.rv-line,.about__rule').forEach(function(el){ io.observe(el); });

  function fmt(v, dec, sep){
    if (dec) return v.toFixed(dec).replace('.', ',');
    var s = Math.round(v).toString();
    return sep ? s.replace(/\B(?=(\d{3})+(?!\d))/g, sep) : s;
  }
  var cio = new IntersectionObserver(function(en){
    en.forEach(function(e){
      if (!e.isIntersecting) return;
      var el = e.target; cio.unobserve(el);
      var to = parseFloat(el.dataset.count), dec = parseInt(el.dataset.dec || '0', 10),
          sep = el.dataset.sep || '', t0 = performance.now();
      if (RM){ el.textContent = fmt(to, dec, sep); return; }
      (function step(t){
        var p = Math.min((t - t0) / 1500, 1), k = 1 - Math.pow(1 - p, 3);
        el.textContent = fmt(to * k, dec, sep);
        if (p < 1) requestAnimationFrame(step);
      })(t0);
    });
  }, { threshold: 0.4 });
  $$('[data-count]').forEach(function(el){ cio.observe(el); });

  /* ============================================================
     parallax
     ============================================================ */
  var parEls = $$('[data-par]'), ticking = false;
  function parallax(){
    ticking = false;
    var vh = innerHeight;
    parEls.forEach(function(el){
      var r = el.getBoundingClientRect();
      if (r.bottom < -200 || r.top > vh + 200) return;
      var mid = r.top + r.height / 2 - vh / 2;
      el.style.transform = 'translate3d(0,' + (-mid * parseFloat(el.dataset.par)).toFixed(2) + 'px,0)';
    });
  }
  if (!RM && parEls.length){
    addEventListener('scroll', function(){ if (!ticking){ ticking = true; requestAnimationFrame(parallax); } }, { passive: true });
    addEventListener('resize', parallax);
    parallax();
  }

  /* ============================================================
     cursor, magnetic buttons, 3D tilt
     ============================================================ */
  if (matchMedia('(hover:hover) and (pointer:fine)').matches && !RM){
    var dot = $('#cursor'), ring = $('#cursor-ring');
    if (dot && ring){
      var mx = innerWidth / 2, my = innerHeight / 2, rx = mx, ry = my;
      addEventListener('mousemove', function(e){
        mx = e.clientX; my = e.clientY;
        dot.style.transform = 'translate3d(' + mx + 'px,' + my + 'px,0)';
      }, { passive: true });
      (function loop(){
        rx += (mx - rx) * 0.16; ry += (my - ry) * 0.16;
        ring.style.transform = 'translate3d(' + rx + 'px,' + ry + 'px,0)';
        requestAnimationFrame(loop);
      })();
      var HOT = 'a,button,.card,.row,.chip,.tile,.step,input,textarea';
      document.addEventListener('mouseover', function(e){ if (e.target.closest(HOT)) document.body.classList.add('cur-hot'); });
      document.addEventListener('mouseout',  function(e){ if (e.target.closest(HOT)) document.body.classList.remove('cur-hot'); });
    }
    $$('[data-mag]').forEach(function(el){
      el.addEventListener('mousemove', function(e){
        var r = el.getBoundingClientRect();
        el.style.transform = 'translate(' + (e.clientX - r.left - r.width / 2) * 0.24 + 'px,' +
                                            (e.clientY - r.top - r.height / 2) * 0.32 + 'px)';
      });
      el.addEventListener('mouseleave', function(){ el.style.transform = ''; });
    });
    document.addEventListener('mousemove', function(e){
      var el = e.target.closest('[data-tilt]');
      if (!el) return;
      var r = el.getBoundingClientRect();
      var px = (e.clientX - r.left) / r.width - 0.5, py = (e.clientY - r.top) / r.height - 0.5;
      el.style.transform = 'perspective(900px) rotateX(' + (-py * 5).toFixed(2) + 'deg) rotateY(' + (px * 6).toFixed(2) + 'deg) translateY(-6px)';
    });
    document.addEventListener('mouseout', function(e){
      var el = e.target.closest('[data-tilt]');
      if (el && !el.contains(e.relatedTarget)) el.style.transform = '';
    });
  }

  /* ============================================================
     mobile menu
     ============================================================ */
  var burger = $('#burger');
  function closeMenu(){ document.body.classList.remove('menu-open','is-locked'); }
  if (burger){
    burger.addEventListener('click', function(){
      var open = document.body.classList.toggle('menu-open');
      document.body.classList.toggle('is-locked', open);
    });
    addEventListener('keydown', function(e){ if (e.key === 'Escape') closeMenu(); });
  }

  /* ============================================================
     toast + copy to clipboard
     ============================================================ */
  var toast = $('#toast'), tt;
  function say(msg){
    if (!toast) return;
    toast.textContent = msg;
    toast.classList.add('on');
    clearTimeout(tt);
    tt = setTimeout(function(){ toast.classList.remove('on'); }, 2200);
  }
  $$('[data-copy]').forEach(function(el){
    el.addEventListener('click', function(){
      var v = el.dataset.copy;
      if (navigator.clipboard && navigator.clipboard.writeText){
        navigator.clipboard.writeText(v).then(function(){ say('Copied'); }, function(){ say(v); });
      } else { say(v); }
    });
  });

  /* ============================================================
     contact form
     ============================================================ */
  var form = $('#form'), submit = $('#submit');
  if (form && submit){
    form.addEventListener('submit', function(e){
      e.preventDefault();
      var name = $('#f-name').value.trim(), mail = $('#f-mail').value.trim(), msg = $('#f-msg').value.trim();
      if (!name || !mail || !msg || mail.indexOf('@') < 1){ say('Please fill in all fields'); return; }
      if (submit.classList.contains('loading')) return;
      submit.classList.add('loading');
      /* demo endpoint — wire to the real handler on integration */
      setTimeout(function(){
        submit.classList.remove('loading');
        submit.classList.add('done');
        $('.lbl', submit).textContent = 'Message sent';
        say('Thank you — our specialist will contact you shortly');
        form.reset();
        setTimeout(function(){ submit.classList.remove('done'); $('.lbl', submit).textContent = 'Send message'; }, 4000);
      }, 1300);
    });
  }

  /* ============================================================
     GSAP extras — optional
     ============================================================ */
  addEventListener('load', function(){
    initSmooth();
    if (RM || !window.gsap || !window.ScrollTrigger) return;
    gsap.registerPlugin(ScrollTrigger);
    if ($('.hero__in')){
      gsap.fromTo('.hero__in', { y: 0 }, {
        y: -70, opacity: .35, ease: 'none',
        scrollTrigger: { trigger: '.hero', start: 'top top', end: 'bottom top', scrub: true }
      });
    }
    ScrollTrigger.refresh();
  });
})();
