/* Home hero: glyph rain in depth, and the link the package makes - AI client -> Matrix server -> Unity Editor -
   with requests (cyan) and results (green) travelling along it once the state reads ONLINE. */
(function () {
  "use strict";
  var hero = document.querySelector(".hero");
  if (!hero) return;
  var reduced = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  var stateEl = hero.querySelector(".state");
  var stateText = stateEl && stateEl.querySelector("b");
  var online = false;

  /* ---------- link state + title decode: runs with or without WebGL ---------- */
  var GLYPHS = "ｦｧｨｩｪｫｬｭｮｯｱｲｳｴｵｶｷｸｹｺｻｼｽｾｿﾀﾁﾂﾃﾄﾅﾆﾇﾈﾉﾊﾋﾌﾍﾎﾏﾐﾑﾒﾓﾔﾕﾖﾗﾘﾙﾚﾛﾜﾝ0123456789";
  function setState(s) {
    if (!stateEl) return;
    stateEl.setAttribute("data-s", s);
    if (stateText) stateText.textContent = s.toUpperCase();
    online = s === "online";
  }
  /* each letter keeps its own box, so the scramble never changes the line layout */
  function decode(el, delay) {
    var finalText = el.textContent, start = performance.now() + delay;
    el.setAttribute("aria-label", finalText);
    el.textContent = "";
    var spans = [];
    for (var i = 0; i < finalText.length; i++) {
      var ch = finalText[i];
      if (ch === " ") { el.appendChild(document.createTextNode(" ")); continue; }
      var sp = document.createElement("span");
      sp.className = "dc s"; sp.textContent = ch; sp.setAttribute("aria-hidden", "true");
      el.appendChild(sp); spans.push(sp);
    }
    var n = spans.length;
    function tick(t) {
      var p = Math.max(0, (t - start) / 900), left = 0;
      for (var i = 0; i < n; i++) {
        if (p * n * 1.4 - i * 0.9 > 1) { if (spans[i].classList.contains("s")) spans[i].classList.remove("s"); }
        else { left++; if (Math.random() < 0.5) spans[i].setAttribute("data-g", GLYPHS[(Math.random() * GLYPHS.length) | 0]); }
      }
      if (left) requestAnimationFrame(tick);
    }
    requestAnimationFrame(tick);
    setTimeout(function () { spans.forEach(function (sp) { sp.classList.remove("s"); }); }, delay + 2500);
  }
  if (reduced) setState("online");
  else {
    setState("offline");
    var lines = hero.querySelectorAll("h1 .ln");
    for (var i = 0; i < lines.length; i++) decode(lines[i], 150 + i * 260);
    setTimeout(function () { setState("linking"); }, 700);
    setTimeout(function () { setState("online"); }, 2100);
  }

  /* ---------- WebGL scene ---------- */
  function start() {
    var THREE = window.THREE;
    var canvas = hero.querySelector(".hero-canvas");
    if (!THREE || !canvas) return;
    var renderer;
    try { renderer = new THREE.WebGLRenderer({ canvas: canvas, antialias: true, powerPreference: "high-performance", preserveDrawingBuffer: reduced }); }
    catch (e) { return; }
    if (!renderer.getContext()) return;
    hero.classList.add("gl");
    renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
    renderer.setClearColor(0x07090a, 1);

    var scene = new THREE.Scene();
    scene.fog = new THREE.FogExp2(0x07090a, 0.028);
    var camera = new THREE.PerspectiveCamera(42, 1, 0.1, 200);
    camera.position.set(0, 0, 22);

    /* glyph atlas: 8 x 8 cells */
    var atlas = document.createElement("canvas");
    atlas.width = atlas.height = 512;
    var g = atlas.getContext("2d");
    g.fillStyle = "#fff"; g.textAlign = "center"; g.textBaseline = "middle";
    g.font = "600 46px 'JetBrains Mono', 'MS Gothic', 'Yu Gothic', monospace";
    for (var c = 0; c < 64; c++) g.fillText(GLYPHS[c % GLYPHS.length], (c % 8) * 64 + 32, Math.floor(c / 8) * 64 + 34);
    var atlasTex = new THREE.CanvasTexture(atlas);
    atlasTex.minFilter = THREE.LinearFilter;

    /* rain: COLS columns x TRAIL glyphs, all moved in the vertex shader */
    var COLS = window.innerWidth < 700 ? 70 : 140, TRAIL = 22;
    var n = COLS * TRAIL;
    var pos = new Float32Array(n * 3), col = new Float32Array(n * 4), idx = new Float32Array(n);
    for (var k = 0; k < COLS; k++) {
      var x = (Math.random() - 0.5) * 70, z = -Math.random() * 46 + 4;
      var speed = 2.2 + Math.random() * 4.5, off = Math.random() * 100, seed = Math.random() * 1000;
      for (var t = 0; t < TRAIL; t++) {
        var j = k * TRAIL + t;
        pos[j * 3] = x; pos[j * 3 + 1] = 0; pos[j * 3 + 2] = z;
        col[j * 4] = speed; col[j * 4 + 1] = off; col[j * 4 + 2] = seed; col[j * 4 + 3] = 0.55 + Math.random() * 0.5;
        idx[j] = t;
      }
    }
    var rainGeo = new THREE.BufferGeometry();
    rainGeo.setAttribute("position", new THREE.BufferAttribute(pos, 3));
    rainGeo.setAttribute("aCol", new THREE.BufferAttribute(col, 4));
    rainGeo.setAttribute("aIdx", new THREE.BufferAttribute(idx, 1));
    var rainMat = new THREE.ShaderMaterial({
      transparent: true, depthWrite: false, blending: THREE.AdditiveBlending,
      uniforms: { uTime: { value: 0 }, uAtlas: { value: atlasTex }, uScale: { value: 1 }, uSpeed: { value: 1 } },
      vertexShader: [
        "attribute vec4 aCol; attribute float aIdx;",
        "uniform float uTime; uniform float uScale; uniform float uSpeed;",
        "varying float vB; varying float vG; varying float vHead;",
        "float h(float n){ return fract(sin(n) * 43758.5453); }",
        "void main(){",
        "  float range = 40.0; float sp = 0.62;",
        "  float head = 20.0 - mod(uTime * aCol.x * uSpeed + aCol.y, range + " + TRAIL + ".0 * sp);",
        "  vec3 p = position; p.y = head + aIdx * sp;",
        "  vec4 mv = modelViewMatrix * vec4(p, 1.0);",
        "  gl_Position = projectionMatrix * mv;",
        "  gl_PointSize = uScale * 15.0 / -mv.z;",
        "  vHead = aIdx < 0.5 ? 1.0 : 0.0;",
        "  vB = pow(1.0 - aIdx / " + TRAIL + ".0, 1.6) * aCol.w;",
        "  float tick = floor(uTime * (2.0 + h(aCol.z) * 6.0) + aIdx * 3.1);",
        "  vG = floor(h(aCol.z + aIdx * 13.7 + tick * 0.71) * 64.0);",
        "}"
      ].join("\n"),
      fragmentShader: [
        "uniform sampler2D uAtlas; varying float vB; varying float vG; varying float vHead;",
        "void main(){",
        "  vec2 cell = vec2(mod(vG, 8.0), floor(vG / 8.0));",
        "  vec2 uv = (cell + vec2(gl_PointCoord.x, gl_PointCoord.y)) / 8.0;",
        "  float a = texture2D(uAtlas, uv).a;",
        "  vec3 green = vec3(0.0, 1.0, 0.26);",
        "  vec3 c = mix(green * 0.85, vec3(0.82, 1.0, 0.88), vHead);",
        "  float alpha = a * (vHead > 0.5 ? 0.9 : vB * 0.75);",
        "  if (alpha < 0.02) discard;",
        "  gl_FragColor = vec4(c * alpha, alpha);",
        "}"
      ].join("\n")
    });
    atlasTex.flipY = false;
    var rain = new THREE.Points(rainGeo, rainMat);
    scene.add(rain);


    /* ---------- the link, drawn with an orthographic camera so each mark keeps its true shape ---------- */
    var CYAN = 0x00e5ff, GREEN = 0x00ff41, AMBER = 0xffc440;
    var ui = new THREE.Scene();
    var ortho = new THREE.OrthographicCamera(0, 1, 0, -1, -2000, 2000);
    ui.add(new THREE.AmbientLight(0xffffff, 0.34));
    var sun = new THREE.DirectionalLight(0xffffff, 1.0);
    sun.position.set(-0.45, 1, 0.55);
    ui.add(sun);
    var glowTex = (function () {
      var cv = document.createElement("canvas"); cv.width = cv.height = 128;
      var x = cv.getContext("2d"), gr = x.createRadialGradient(64, 64, 0, 64, 64, 64);
      gr.addColorStop(0, "rgba(255,255,255,1)"); gr.addColorStop(0.22, "rgba(255,255,255,0.35)"); gr.addColorStop(1, "rgba(255,255,255,0)");
      x.fillStyle = gr; x.fillRect(0, 0, 128, 128);
      return new THREE.CanvasTexture(cv);
    })();
    function halo(color, size, opacity) {
      var s = new THREE.Sprite(new THREE.SpriteMaterial({ map: glowTex, color: color, transparent: true, blending: THREE.AdditiveBlending, depthWrite: false, depthTest: false, opacity: opacity }));
      s.scale.set(size, size, 1); s.renderOrder = -1; return s;
    }

    /* Unity: the cube mark. A frame of 12 beams seen along (1,1,1); the mark's three cuts sit on the
       vertices top, lower left and lower right, each cut through two of the beams that meet there. */
    var unityRoot = new THREE.Group(), unitySway = new THREE.Group(), unity = new THREE.Group();
    (function () {
      var T = 0.5, GAP = 0.13;
      var mat = new THREE.MeshLambertMaterial({ color: 0xffffff });
      var cuts = { "-1,1,-1": "xz", "1,-1,-1": "yz", "-1,-1,1": "xy" };
      var c = [];
      for (var i = 0; i < 8; i++) c.push(new THREE.Vector3(i & 1 ? 1 : -1, i & 2 ? 1 : -1, i & 4 ? 1 : -1));
      function key(v) { return v.x + "," + v.y + "," + v.z; }
      for (var a = 0; a < 8; a++) for (var b = a + 1; b < 8; b++) {
        var d = c[b].clone().sub(c[a]);
        if (d.length() !== 2) continue;
        var axis = d.x ? "x" : d.y ? "y" : "z";
        var lowC = c[a][axis] < c[b][axis] ? c[a] : c[b], highC = lowC === c[a] ? c[b] : c[a];
        var lo = -1 - T / 2, hi = 1 + T / 2;
        var cl = cuts[key(lowC)], ch = cuts[key(highC)];
        if (cl && cl.indexOf(axis) > -1) lo = -1 + T / 2 + GAP;
        if (ch && ch.indexOf(axis) > -1) hi = 1 - T / 2 - GAP;
        var size = { x: T, y: T, z: T }; size[axis] = hi - lo;
        var beam = new THREE.Mesh(new THREE.BoxGeometry(size.x, size.y, size.z), mat);
        beam.position.copy(c[a]).add(c[b]).multiplyScalar(0.5);
        beam.position[axis] = (hi + lo) / 2;
        unity.add(beam);
      }
      /* object -> view: (1,1,1) toward the viewer, the cube's y axis up on screen */
      var zA = new THREE.Vector3(1, 1, 1).normalize(), xA = new THREE.Vector3(1, 0, -1).normalize(), yA = new THREE.Vector3().crossVectors(zA, xA);
      var m = new THREE.Matrix4().makeBasis(xA, yA, zA).transpose();
      unity.setRotationFromMatrix(m);
    })();
    unitySway.add(unity);
    unityRoot.add(halo(0xffffff, 5.5, 0.1), unitySway);

    /* Matrix server: the package mark, a chevron and a bar */
    var serverRoot = new THREE.Group(), server = new THREE.Group();
    (function () {
      var chev = new THREE.Shape();
      chev.moveTo(-1.5, 1.2); chev.lineTo(-0.15, 0); chev.lineTo(-1.5, -1.2); chev.lineTo(-1.05, -1.62);
      chev.lineTo(0.75, 0); chev.lineTo(-1.05, 1.62); chev.lineTo(-1.5, 1.2);
      var bar = new THREE.Shape();
      bar.moveTo(0.95, -1.5); bar.lineTo(1.6, -1.5); bar.lineTo(1.6, 1.5); bar.lineTo(0.95, 1.5); bar.lineTo(0.95, -1.5);
      var geo = new THREE.ExtrudeGeometry([chev, bar], { depth: 0.6, bevelEnabled: true, bevelThickness: 0.06, bevelSize: 0.05, bevelSegments: 2 });
      geo.center();
      server.add(new THREE.Mesh(geo, new THREE.MeshLambertMaterial({ color: 0x00ff41, emissive: 0x00a82a, emissiveIntensity: 0.65 })));
      var frame = new THREE.Mesh(new THREE.BoxGeometry(4.4, 4.4, 0.35), new THREE.MeshLambertMaterial({ color: 0x0b1a11 }));
      frame.position.z = -0.6;
      var frameEdge = new THREE.LineSegments(new THREE.EdgesGeometry(new THREE.BoxGeometry(4.4, 4.4, 0.35)), new THREE.LineBasicMaterial({ color: 0x2f8a52 }));
      frameEdge.position.z = -0.6;
      server.add(frame, frameEdge);
    })();
    var ring = new THREE.Mesh(new THREE.TorusGeometry(3.4, 0.045, 8, 160), new THREE.MeshBasicMaterial({ color: GREEN, transparent: true, opacity: 0.9 }));
    var beads = [];
    for (var bI = 0; bI < 4; bI++) { var bd = halo(GREEN, 0.8, 1); bd.userData.a = bI / 4 * Math.PI * 2; ring.add(bd); beads.push(bd); }
    serverRoot.add(halo(GREEN, 9, 0.28), server, ring);

    /* AI client: a pixel agent typing on a laptop seen from behind. Sizes are in art pixels: a 15 x 10 sprite,
       centred on the origin; the arms tap the keys, the eyes read along a line and data bits rise off the laptop. */
    var clientRoot = new THREE.Group(), bot = new THREE.Group(), body = new THREE.Group(), eyes = new THREE.Group();
    var arms = [], bits = [], logo;
    (function () {
      var D = 1.6, ZS = D / 2 + 2.4;
      function at(x, y, w, h) { return [x + w / 2 - 7.5, 10.75 - (y + h / 2)]; }
      function block(x, y, w, h, depth, mat, parent, z) {
        var m = new THREE.Mesh(new THREE.BoxGeometry(w, h, depth), mat), p = at(x, y, w, h);
        m.position.set(p[0], p[1], z || 0); (parent || body).add(m); return m;
      }
      var skin = new THREE.MeshLambertMaterial({ color: 0xde886d, emissive: 0x5a2616, emissiveIntensity: 0.55 });
      block(2, 6, 11, 7, D, skin);
      [3, 5, 9, 11].forEach(function (x) { block(x, 13, 1, 2, D, skin); });
      [[2, 1], [13, -1]].forEach(function (s) {
        var arm = new THREE.Group(), p = at(s[0], 10, 0, 0);
        arm.position.set(p[0], p[1], 0);
        var m = new THREE.Mesh(new THREE.BoxGeometry(2, 2, D), skin);
        m.position.set(-s[1], 0, 0); arm.add(m); arm.userData.side = s[1];
        body.add(arm); arms.push(arm);
      });
      var dark = new THREE.MeshBasicMaterial({ color: 0x050505 });
      [4, 10].forEach(function (x) { var e = new THREE.Mesh(new THREE.PlaneGeometry(1, 1), dark), p = at(x, 8.5, 1, 1); e.position.set(p[0], p[1], D / 2 + 0.01); eyes.add(e); });
      body.add(eyes);

      var laptop = new THREE.Group();
      block(3, 9.5, 9, 5.5, 0.3, new THREE.MeshLambertMaterial({ color: 0x78909c }), laptop, ZS);
      block(2.5, 14.5, 10, 1, 3, new THREE.MeshLambertMaterial({ color: 0x546e7a }), laptop, ZS - 1.35);
      logo = new THREE.Mesh(new THREE.PlaneGeometry(1, 1), new THREE.MeshBasicMaterial({ color: 0xffffff, transparent: true }));
      var lp = at(7, 11.5, 1, 1); logo.position.set(lp[0], lp[1], ZS + 0.16);
      logo.userData.halo = halo(CYAN, 4, 0.5); logo.userData.halo.position.copy(logo.position);
      laptop.add(logo, logo.userData.halo);

      var bitMat = new THREE.MeshBasicMaterial({ color: 0x40c4ff, transparent: true, opacity: 0 });
      [[-2, 12, 0], [5, 11, 0.3], [12, 13, 0.6], [17, 11, 0.8], [8, 10, 0.1], [1, 11, 0.4], [15, 12, 0.7]].forEach(function (b) {
        var m = new THREE.Mesh(new THREE.BoxGeometry(1.5, 1.5, 0.4), bitMat.clone()), p = at(b[0], b[1], 1.5, 1.5);
        m.position.set(p[0], p[1], ZS - 0.6); m.userData.y = p[1]; m.userData.delay = b[2];
        laptop.add(m); bits.push(m);
      });
      bot.add(body, laptop);
    })();
    clientRoot.add(halo(0xde886d, 26, 0.14), bot);
    ui.add(clientRoot, serverRoot, unityRoot);

    /* links and packets in screen space */
    var curves = [0, 1].map(function () { return new THREE.CubicBezierCurve3(new THREE.Vector3(), new THREE.Vector3(), new THREE.Vector3(), new THREE.Vector3()); });
    var tubeMat = new THREE.MeshBasicMaterial({ color: CYAN, transparent: true, opacity: 0, blending: THREE.AdditiveBlending, depthWrite: false });
    var tubes = curves.map(function () { var t = new THREE.Mesh(new THREE.BufferGeometry(), tubeMat); t.renderOrder = -1; ui.add(t); return t; });
    var PK = 26, pkPos = new Float32Array(PK * 3), pkCol = new Float32Array(PK * 3), pk = [];
    for (var q = 0; q < PK; q++) {
      var req = q % 3 !== 0;
      pk.push({ req: req, u: Math.random(), speed: 0.14 + Math.random() * 0.1 });
      var cc = new THREE.Color(req ? CYAN : GREEN);
      pkCol[q * 3] = cc.r; pkCol[q * 3 + 1] = cc.g; pkCol[q * 3 + 2] = cc.b;
    }
    var pkGeo = new THREE.BufferGeometry();
    pkGeo.setAttribute("position", new THREE.BufferAttribute(pkPos, 3));
    pkGeo.setAttribute("color", new THREE.BufferAttribute(pkCol, 3));
    var pkMat = new THREE.PointsMaterial({ size: 16, sizeAttenuation: false, map: glowTex, vertexColors: true, transparent: true, depthWrite: false, blending: THREE.AdditiveBlending, opacity: 0 });
    var packets = new THREE.Points(pkGeo, pkMat);
    packets.frustumCulled = false; packets.renderOrder = -1;
    ui.add(packets);

    var labels = Array.prototype.slice.call(hero.querySelectorAll(".node-label"));
    var W = 1, H = 1, mobile = false, U = 40;
    var spots = [];
    function layout() {
      W = hero.clientWidth; H = hero.clientHeight;
      renderer.setSize(W, H, false);
      camera.aspect = W / H; camera.updateProjectionMatrix();
      ortho.left = 0; ortho.right = W; ortho.top = 0; ortho.bottom = -H; ortho.updateProjectionMatrix();
      mobile = W / H < 0.95;
      /* U = pixels per unit; each mark is sized in units */
      if (mobile) {
        U = Math.min(W / 22, 19);
        spots = [[W * 0.18, 150], [W * 0.5, 140], [W * 0.82, 150]];
      } else {
        U = Math.min(H / 15, W / 26, 54);
        spots = [[W * 0.6, H * 0.24], [W * 0.74, H * 0.47], [W * 0.875, H * 0.7]];
      }
      clientRoot.scale.setScalar(U * 0.33);
      serverRoot.scale.setScalar(U * 0.9);
      unityRoot.scale.setScalar(U * 1.12);
      [clientRoot, serverRoot, unityRoot].forEach(function (o, i) { o.position.set(spots[i][0], -spots[i][1], 0); });
      for (var i = 0; i < 2; i++) {
        var a = new THREE.Vector3(spots[i][0], -spots[i][1], -500), b = new THREE.Vector3(spots[i + 1][0], -spots[i + 1][1], -500);
        var lift = mobile ? 50 : 90;
        curves[i].v0.copy(a); curves[i].v3.copy(b);
        curves[i].v1.set(a.x + (b.x - a.x) * 0.15, a.y + lift, -500);
        curves[i].v2.set(b.x - (b.x - a.x) * 0.15, b.y + lift, -500);
        tubes[i].geometry.dispose();
        tubes[i].geometry = new THREE.TubeGeometry(curves[i], 80, mobile ? 1.5 : 2.2, 6, false);
      }
      pkMat.size = mobile ? 11 : 16;
      rainMat.uniforms.uScale.value = H * (mobile ? 0.9 : 0.75) / 10;
      var under = [1.95 * U, 2.75 * U, 2.75 * U];
      labels.forEach(function (el, i) {
        var o = [clientRoot, serverRoot, unityRoot][i];
        el.style.left = spots[i][0] + "px";
        el.style.top = (spots[i][1] + under[i]) + "px";
        el.classList.add("on");
      });
      if (reduced) render(0);
    }

    var px = 0, py = 0, tx = 0, ty = 0, scrollP = 0, visible = true, elapsed = 0, linkOn = 0, tmp = new THREE.Vector3();
    window.addEventListener("pointermove", function (e) { tx = (e.clientX / window.innerWidth - 0.5); ty = (e.clientY / window.innerHeight - 0.5); }, { passive: true });
    window.addEventListener("scroll", function () { scrollP = Math.min(1, window.scrollY / Math.max(1, H)); }, { passive: true });
    window.addEventListener("resize", layout);
    if ("ResizeObserver" in window) new ResizeObserver(function () { if (hero.clientHeight !== H || hero.clientWidth !== W) layout(); }).observe(hero);
    if ("IntersectionObserver" in window) new IntersectionObserver(function (l) { visible = l[0].isIntersecting; }).observe(hero);

    function render(dt) {
      elapsed += dt;
      px += (tx - px) * 0.05; py += (ty - py) * 0.05;
      rainMat.uniforms.uTime.value = elapsed;
      rainMat.uniforms.uSpeed.value = 1 - scrollP * 0.6;
      camera.position.x = px * 1.2; camera.position.y = -py * 0.8;
      camera.lookAt(camera.position.x * 0.5, camera.position.y * 0.5, 0);

      /* Unity: the mark stays square-on; it only turns a little with the pointer and on scroll */
      unitySway.rotation.y = Math.sin(elapsed * 0.5) * 0.1 + px * 0.25 + scrollP * 0.8;
      unitySway.rotation.x = py * 0.15;

      /* server: the mark tilts gently, the ring turns */
      server.rotation.y = Math.sin(elapsed * 0.6) * 0.3 + px * 0.3;
      server.rotation.x = Math.sin(elapsed * 0.45) * 0.1;
      ring.rotation.x = 1.25; ring.rotation.z += dt * 0.45;
      beads.forEach(function (b) { b.position.set(Math.cos(b.userData.a) * 3.4, Math.sin(b.userData.a) * 3.4, 0); });

      /* agent: types fast, reads along a line of code and snaps back, data rises off the laptop */
      bot.rotation.y = -0.2 + Math.sin(elapsed * 0.8) * 0.08 + px * 0.25;
      bot.rotation.x = 0.06;
      body.position.y = (elapsed % 0.16) < 0.08 ? 0 : -0.15;
      arms.forEach(function (a, i) {
        a.rotation.y = a.userData.side * 1.25;
        a.rotation.x = Math.sin(elapsed * (i ? 52 : 42)) * 0.3;
      });
      var line = (elapsed % 1.2) / 1.2;
      eyes.position.x = line < 0.85 ? Math.min(2, Math.floor(line / 0.15) - 2) : -2;
      logo.material.opacity = 0.7 + Math.sin(elapsed * 2.1) * 0.3;
      logo.userData.halo.material.opacity = logo.material.opacity * 0.4;
      bits.forEach(function (b) {
        var t = (elapsed + b.userData.delay) % 1;
        b.position.y = b.userData.y + 15 * t;
        b.scale.setScalar(0.5 + 0.7 * t);
        b.material.opacity = t < 0.2 ? t * 4 : 0.8 * (1 - t) / 0.8;
      });

      linkOn += ((online ? 1 : 0) - linkOn) * Math.min(1, dt * 2.5);
      tubeMat.opacity = 0.1 + linkOn * 0.32;
      tubeMat.color.setHex(online ? CYAN : AMBER);
      ring.material.color.setHex(online ? GREEN : AMBER);
      pkMat.opacity = linkOn;
      for (var i = 0; i < PK; i++) {
        var p = pk[i];
        p.u += dt * p.speed; if (p.u > 1) p.u -= 1;
        var u = p.req ? p.u : 1 - p.u, seg = u < 0.5 ? 0 : 1;
        curves[seg].getPoint(seg ? (u - 0.5) * 2 : u * 2, tmp);
        pkPos[i * 3] = tmp.x; pkPos[i * 3 + 1] = tmp.y + (p.req ? 4 : -4); pkPos[i * 3 + 2] = -400;
      }
      pkGeo.attributes.position.needsUpdate = true;

      renderer.autoClear = true;
      renderer.render(scene, camera);
      renderer.autoClear = false;
      renderer.clearDepth();
      renderer.render(ui, ortho);
    }
    if (reduced) { elapsed = 2; online = true; linkOn = 1; }
    layout();
    if (reduced) { setTimeout(function () { render(0); }, 300); return; }
    var last = 0;
    function loop(t) {
      requestAnimationFrame(loop);
      var dt = last ? Math.min((t - last) / 1000, 0.05) : 0.016;
      last = t;
      if (!visible || document.hidden) return;
      render(dt);
    }
    requestAnimationFrame(loop);
  }

  if (window.THREE) start();
  else {
    var s = document.querySelector("script[data-three]");
    if (s) s.addEventListener("load", start);
  }
})();
