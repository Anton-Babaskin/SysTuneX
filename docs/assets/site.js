(function(){
  var reduce = matchMedia('(prefers-reduced-motion: reduce)').matches;

  // Sticky header hairline.
  var head = document.querySelector('header');
  addEventListener('scroll', function(){
    head.classList.toggle('stuck', scrollY > 8);
  }, {passive:true});

  // Reveal on scroll.
  var reveals = document.querySelectorAll('.reveal');
  if (!('IntersectionObserver' in window)) {
    // No observer, no hiding: show everything rather than leave the page blank.
    document.documentElement.classList.remove('js');
  } else {
    var io = new IntersectionObserver(function(entries){
      entries.forEach(function(e){ if(e.isIntersecting){ e.target.classList.add('in'); io.unobserve(e.target); } });
    }, {rootMargin:'0px 0px -60px 0px'});
    reveals.forEach(function(el){ io.observe(el); });
  }

  // A frame-time strip, because this is what the application is for.
  var bars = document.getElementById('bars');
  if (bars) {
    var N = 46, heights = [];
    for (var i=0;i<N;i++){ var b=document.createElement('i'); bars.appendChild(b); heights.push(40); }
    var els = bars.children;
    if (!reduce) {
      var t = 0;
      setInterval(function(){
        t++;
        // Mostly steady with the occasional spike - what a real frame-time graph looks like.
        var v = 34 + Math.sin(t/7)*10 + Math.random()*14;
        if (Math.random() < 0.06) v += 34;
        heights.push(Math.min(100, v)); heights.shift();
        for (var i=0;i<N;i++){ els[i].style.height = heights[i] + '%'; }

        var fps = Math.round(1000 / (4.5 + heights[N-1]/18));
        document.getElementById('fpsval').textContent = fps;
        document.getElementById('lowval').textContent = Math.round(fps * 0.82);
        document.getElementById('ftval').textContent  = (1000/fps).toFixed(1);
      }, 110);
    } else {
      for (var j=0;j<N;j++){ els[j].style.height = (30 + (j%7)*6) + '%'; }
    }
  }

  // The real version and size, so the page cannot go stale behind a release.
  fetch('https://api.github.com/repos/Anton-Babaskin/SysTuneX/releases/latest')
    .then(function(r){ return r.ok ? r.json() : Promise.reject(); })
    .then(function(d){
      if (d.tag_name) document.getElementById('ver').textContent = d.tag_name.replace(/^v/,'');
      var exe = (d.assets||[]).filter(function(a){ return a.name === 'SysTuneX.exe'; })[0];
      var size = document.getElementById('size');
      if (exe && size) size.textContent = Math.round(exe.size/1048576) + ' ' + (size.dataset.unit || 'MB');
    })
    .catch(function(){ /* The hard-coded values in the markup stay. */ });
})();
