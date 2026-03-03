const uploadInput = document.getElementById("uploadInput");
const uploadBtn = document.getElementById("uploadBtn");
const icaoDetectBtn = document.getElementById("icaoDetectBtn");
const icaoPreview = document.getElementById("icaoPreview");
const icaoSummary = document.getElementById("icaoSummary");
const icaoPercent = document.getElementById("icaoPercent");
const icaoTotals = document.getElementById("icaoTotals");
const icaoSections = document.getElementById("icaoSections");
const startWebcamBtn = document.getElementById("startWebcamBtn");
const captureDetectBtn = document.getElementById("captureDetectBtn");
const startLivenessBtn = document.getElementById("startLivenessBtn");
const webcam = document.getElementById("webcam");
const canvas = document.getElementById("snapshot");
const instructionText = document.getElementById("instructionText");
const resultJson = document.getElementById("resultJson");
const transactionIdInput = document.getElementById("transactionIdInput");
const matchInput1 = document.getElementById("matchInput1");
const matchInput2 = document.getElementById("matchInput2");
const matchBtn = document.getElementById("matchBtn");
const matchPreview1 = document.getElementById("matchPreview1");
const matchPreview2 = document.getElementById("matchPreview2");
const matchScore1 = document.getElementById("matchScore1");
const matchScore2 = document.getElementById("matchScore2");
const docInput = document.getElementById("docInput");
const docScenario = document.getElementById("docScenario");
const docProcessBtn = document.getElementById("docProcessBtn");
const docFraudBtn = document.getElementById("docFraudBtn");
const fraudDocInput = document.getElementById("fraudDocInput");
const fraudScenario = document.getElementById("fraudScenario");
const fraudDetectBtn = document.getElementById("fraudDetectBtn");
const fraudSummary = document.getElementById("fraudSummary");
const fraudTransaction = document.getElementById("fraudTransaction");
const fraudOverall = document.getElementById("fraudOverall");
const fraudChecks = document.getElementById("fraudChecks");
const fraudNotApplicable = document.getElementById("fraudNotApplicable");
const fraudNotApplicableList = document.getElementById("fraudNotApplicableList");
const verifyDocInput = document.getElementById("verifyDocInput");
const verifyIdentityBtn = document.getElementById("verifyIdentityBtn");
const verifyPreviewDoc = document.getElementById("verifyPreviewDoc");
const verifyPreviewLive = document.getElementById("verifyPreviewLive");
const verifyScoreDoc = document.getElementById("verifyScoreDoc");
const verifyScoreLive = document.getElementById("verifyScoreLive");
const stepperPopupBtn = document.getElementById("stepperPopupBtn");
const passportUploadInput = document.getElementById("passportUploadInput");
const comparePassportsBtn = document.getElementById("comparePassportsBtn");
const passportPreview1 = document.getElementById("passportPreview1");
const passportPreview2 = document.getElementById("passportPreview2");
const passportSummary = document.getElementById("passportSummary");
const passportFaceScore = document.getElementById("passportFaceScore");
const passportNumberMatch = document.getElementById("passportNumberMatch");
const passportNameMatch = document.getElementById("passportNameMatch");
const passportDobMatch = document.getElementById("passportDobMatch");
const passportCameraWrap = document.getElementById("passportCameraWrap");
const passportCamera = document.getElementById("passportCamera");
const passportOverlay = document.getElementById("passportOverlay");
const passportInstruction = document.getElementById("passportInstruction");

let stream = null;
let passportStream = null;
let passportFirstBase64 = null;
let passportDetectHandle = null;
let passportStableFrames = 0;
let passportLastTarget = null;
let passportCaptureInProgress = false;
let cvReady = false;
let passportCaptureCanvas = null;
let passportCaptureCtx = null;
let passportLastProcessTime = 0;
let passportLastDebugTime = 0;
const passportDebug = true;

function initOpenCvReady() {
  if (!window.cv) {
    return false;
  }

  if (typeof cv.Mat === "function") {
    cvReady = true;
    return true;
  }

  cv.onRuntimeInitialized = () => {
    cvReady = true;
  };

  return false;
}

if (!initOpenCvReady()) {
  const cvInterval = setInterval(() => {
    if (initOpenCvReady()) {
      clearInterval(cvInterval);
    }
  }, 200);
}

const setResult = (data) => {
  resultJson.textContent = JSON.stringify(data, null, 2);
};

if (stepperPopupBtn) {
  stepperPopupBtn.addEventListener("click", () => {
    window.open(
      "/stepper.html",
      "stepperPoc",
      "width=960,height=720,menubar=no,location=no,status=no,toolbar=no"
    );
  });
}

uploadBtn.addEventListener("click", async () => {
  const file = uploadInput.files[0];
  if (!file) {
    setResult({ error: "Select an image first." });
    return;
  }

  const formData = new FormData();
  formData.append("image", file);

  const response = await fetch("/api/detect-face", {
    method: "POST",
    body: formData,
  });

  const data = await response.json();
  setResult(data);
});

icaoDetectBtn.addEventListener("click", async () => {
  const file = uploadInput.files[0];
  if (!file) {
    setResult({ error: "Select an image first." });
    return;
  }

  const previewUrl = await fileToDataUrl(file);
  setIcaoPreview(previewUrl);

  const formData = new FormData();
  formData.append("image", file);

  const response = await fetch("/api/icao-detect", {
    method: "POST",
    body: formData,
  });

  const data = await response.json();
  setResult(data);
  renderIcaoSummary(data);
});

startWebcamBtn.addEventListener("click", async () => {
  if (stream) {
    return;
  }

  try {
    stream = await navigator.mediaDevices.getUserMedia({ video: true, audio: false });
    webcam.srcObject = stream;
  } catch (error) {
    setResult({ error: "Unable to access webcam.", details: String(error) });
  }
});

captureDetectBtn.addEventListener("click", async () => {
  const frame = captureFrame();
  if (!frame) {
    setResult({ error: "Start webcam first." });
    return;
  }

  const response = await fetch("/api/detect-face", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ imageBase64: frame }),
  });

  const data = await response.json();
  setResult(data);
});

startLivenessBtn.addEventListener("click", async () => {
  const transactionId = transactionIdInput.value.trim();
  if (transactionId) {
    instructionText.textContent = "Checking transaction...";
    const response = await fetch("/api/liveness-detection", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ transactionId }),
    });

    const data = await response.json();
    setResult(data);
    instructionText.textContent = "Idle";
    return;
  }

  const instructions = [
    "Look left",
    "Look right",
    "Look up",
    "Look down",
  ];

  const frames = [];
  for (const instruction of instructions) {
    instructionText.textContent = instruction;
    await delay(3000);
    const frame = captureFrame();
    if (frame) {
      frames.push(frame);
    }
  }

  instructionText.textContent = "Processing...";

  const response = await fetch("/api/liveness-detection", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ frames }),
  });

  const data = await response.json();
  setResult(data);
  instructionText.textContent = "Idle";
});

matchBtn.addEventListener("click", async () => {
  const file1 = matchInput1.files[0];
  const file2 = matchInput2.files[0];
  const missing1 = !file1;
  const missing2 = !file2;

  if ((missing1 || missing2) && !stream) {
    setResult({ error: "Select two images or start the webcam to use a live frame." });
    return;
  }

  if (!missing1 && !missing2) {
    const preview1 = await fileToDataUrl(file1);
    const preview2 = await fileToDataUrl(file2);
    setMatchPreview(preview1, preview2);

    const formData = new FormData();
    formData.append("image1", file1);
    formData.append("image2", file2);

    const response = await fetch("/api/face-match", {
      method: "POST",
      body: formData,
    });

    const data = await response.json();
    //setResult(data);
    showMatchScore(data);
    return;
  }

  const imageBase64_1 = missing1 ? captureFrame() : await fileToBase64(file1);
  const imageBase64_2 = missing2 ? captureFrame() : await fileToBase64(file2);
  const preview1 = missing1
    ? base64ToDataUrl(imageBase64_1)
    : await fileToDataUrl(file1);
  const preview2 = missing2
    ? base64ToDataUrl(imageBase64_2)
    : await fileToDataUrl(file2);
  setMatchPreview(preview1, preview2);

  if (!imageBase64_1 || !imageBase64_2) {
    setResult({ error: "Unable to capture webcam frame. Make sure the webcam is running." });
    return;
  }

  const response = await fetch("/api/face-match", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ imageBase64_1, imageBase64_2 }),
  });

  const data = await response.json();
  //setResult(data);
  showMatchScore(data);
});

docProcessBtn.addEventListener("click", async () => {
  await submitDocument("/api/documents/process");
});

if (docFraudBtn) {
  docFraudBtn.addEventListener("click", async () => {
    await submitDocument("/api/documents/fraud-detection");
  });
}

if (fraudDetectBtn) {
  fraudDetectBtn.addEventListener("click", async () => {
    const files = Array.from(fraudDocInput?.files || []);
    if (files.length === 0) {
      setResult({ error: "Select document images or a PDF." });
      return;
    }

    const formData = new FormData();
    files.forEach((file) => formData.append("images", file));

    const scenario = fraudScenario?.value?.trim();
    if (scenario) {
      formData.append("scenario", scenario);
    }

    const response = await fetch("/api/document-fraud/detect", {
      method: "POST",
      body: formData,
    });

    const data = await response.json();
    setResult(data);
    renderFraudSummary(data);
  });
}

verifyIdentityBtn.addEventListener("click", async () => {
  const files = Array.from(verifyDocInput.files || []);
  if (files.length === 0) {
    setResult({ error: "Select document images or a PDF." });
    return;
  }

  if (!stream) {
    setResult({ error: "Start the webcam to capture the live face." });
    return;
  }

  const livePortrait = captureFrame();
  if (!livePortrait) {
    setResult({ error: "Unable to capture the live face. Try again." });
    return;
  }

  setVerifyPreview(
    "",
    base64ToDataUrl(livePortrait),
    null
  );

  const formData = new FormData();
  files.forEach((file) => formData.append("images", file));
  formData.append("livePortrait", livePortrait);

  const response = await fetch("/api/documents/verify-identity", {
    method: "POST",
    body: formData,
  });

  const data = await response.json();
  setResult(data);
  const similarity = data?.similarityPercent ?? data?.similarity ?? data?.Similarity ?? null;
  const docPortraitBase64 = data?.documentPortraitBase64 ?? data?.documentPortrait ?? null;
  setVerifyPreview(
    docPortraitBase64 ? base64ToDataUrl(docPortraitBase64) : "",
    base64ToDataUrl(livePortrait),
    similarity
  );
});

if (comparePassportsBtn) {
  comparePassportsBtn.hidden = true;
  comparePassportsBtn.disabled = true;
}

if (passportUploadInput) {
  passportUploadInput.addEventListener("change", async () => {
    const file = passportUploadInput.files[0];
    if (!file) {
      passportFirstBase64 = null;
      stopPassportCamera();
      updatePassportInstruction("Upload a document to start the camera.");
      return;
    }

    const preview = await fileToDataUrl(file);
    if (passportPreview1) {
      passportPreview1.src = preview;
    }

    passportFirstBase64 = await fileToBase64(file);
    updatePassportInstruction("Starting camera...");
    await startPassportCamera();
  });
}

function captureFrame() {
  if (!stream) {
    return null;
  }

  const context = canvas.getContext("2d");
  context.drawImage(webcam, 0, 0, canvas.width, canvas.height);
  const dataUrl = canvas.toDataURL("image/jpeg", 0.92);
  return dataUrl.split(",")[1];
}

function delay(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function fileToBase64(file) {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => {
      const result = reader.result || "";
      const base64 = String(result).split(",")[1];
      resolve(base64 || "");
    };
    reader.onerror = () => reject(reader.error);
    reader.readAsDataURL(file);
  });
}

function fileToDataUrl(file) {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result || ""));
    reader.onerror = () => reject(reader.error);
    reader.readAsDataURL(file);
  });
}

function base64ToDataUrl(base64) {
  if (!base64) {
    return "";
  }
  return `data:image/jpeg;base64,${base64}`;
}

function updatePassportInstruction(text) {
  if (passportInstruction) {
    passportInstruction.textContent = text;
  }
}

async function startPassportCamera() {
  if (!passportCamera || !passportOverlay || passportStream) {
    return;
  }

  passportCaptureInProgress = false;
  passportStableFrames = 0;
  passportLastTarget = null;
  updatePassportInstruction("Loading document detector...");

  try {
    passportStream = await navigator.mediaDevices.getUserMedia({
      video: {
        facingMode: "environment",
        width: { ideal: 1280 },
        height: { ideal: 720 },
      },
      audio: false,
    });
    passportCamera.srcObject = passportStream;
    if (passportCameraWrap) {
      passportCameraWrap.hidden = false;
    }

    passportCamera.addEventListener(
      "loadedmetadata",
      () => {
        resizePassportCanvases();
        startPassportDetectionLoop();
      },
      { once: true }
    );
  } catch (error) {
    updatePassportInstruction("Unable to access the camera.");
    setResult({ error: "Unable to access the camera.", details: String(error) });
  }
}

function stopPassportCamera() {
  if (passportDetectHandle) {
    cancelAnimationFrame(passportDetectHandle);
    passportDetectHandle = null;
  }

  if (passportStream) {
    passportStream.getTracks().forEach((track) => track.stop());
    passportStream = null;
  }

  if (passportCamera) {
    passportCamera.srcObject = null;
  }

  if (passportCameraWrap) {
    passportCameraWrap.hidden = true;
  }

  if (passportOverlay) {
    const ctx = passportOverlay.getContext("2d");
    ctx.clearRect(0, 0, passportOverlay.width, passportOverlay.height);
  }
}

function resizePassportCanvases() {
  const width = passportCamera.videoWidth || 640;
  const height = passportCamera.videoHeight || 480;

  passportOverlay.width = width;
  passportOverlay.height = height;

  passportCaptureCanvas = passportCaptureCanvas || document.createElement("canvas");
  passportCaptureCanvas.width = width;
  passportCaptureCanvas.height = height;
  passportCaptureCtx = passportCaptureCanvas.getContext("2d", { willReadFrequently: true });
}

function startPassportDetectionLoop() {
  if (!passportCamera || !passportOverlay) {
    return;
  }

  const loop = () => {
    passportDetectHandle = requestAnimationFrame(loop);
    detectPassportFrame();
  };
  loop();
}

function detectPassportFrame() {
  if (!passportStream || passportCaptureInProgress) {
    return;
  }

  if (!cvReady) {
    drawPassportGuide();
    return;
  }

  const now = performance.now();
  if (now - passportLastProcessTime < 120) {
    return;
  }
  passportLastProcessTime = now;

  const width = passportOverlay.width;
  const height = passportOverlay.height;
  passportCaptureCtx.drawImage(passportCamera, 0, 0, width, height);

  const src = cv.imread(passportCaptureCanvas);
  const gray = new cv.Mat();
  const blurred = new cv.Mat();
  const edges = new cv.Mat();
  const dilated = new cv.Mat();
  const contours = new cv.MatVector();
  const hierarchy = new cv.Mat();

  cv.cvtColor(src, gray, cv.COLOR_RGBA2GRAY);
  cv.GaussianBlur(gray, blurred, new cv.Size(5, 5), 0);
  cv.Canny(blurred, edges, 75, 200);
  const kernel = cv.getStructuringElement(cv.MORPH_RECT, new cv.Size(3, 3));
  cv.dilate(edges, dilated, kernel);
  kernel.delete();
  cv.findContours(dilated, contours, hierarchy, cv.RETR_EXTERNAL, cv.CHAIN_APPROX_SIMPLE);

  const frameArea = width * height;
  let best = null;
  let bestArea = 0;
  let bestRect = null;
  let bestBounds = null;

  if (passportDebug && performance.now() - passportLastDebugTime > 1000) {
    passportLastDebugTime = performance.now();
    console.log("[passport] contours:", contours.size(), "area:", frameArea);
  }

  for (let i = 0; i < contours.size(); i++) {
    const contour = contours.get(i);
    const area = cv.contourArea(contour);
    if (area < frameArea * 0.01) {
      contour.delete();
      continue;
    }

    const peri = cv.arcLength(contour, true);
    const approx = new cv.Mat();
    cv.approxPolyDP(contour, approx, 0.02 * peri, true);
    const isQuad = approx.rows === 4 && cv.isContourConvex(approx);
    if (area > bestArea) {
      if (best) {
        best.delete();
      }
      best = approx;
      bestArea = area;
      if (!isQuad) {
        bestRect = cv.minAreaRect(contour);
        bestBounds = cv.boundingRect(contour);
      } else {
        bestRect = null;
        bestBounds = cv.boundingRect(contour);
      }
    } else {
      approx.delete();
    }
    contour.delete();
  }

  drawPassportOverlay(best, bestRect, bestBounds, bestArea, frameArea);

  if (best) {
    best.delete();
  }

  src.delete();
  gray.delete();
  blurred.delete();
  edges.delete();
  dilated.delete();
  contours.delete();
  hierarchy.delete();
}

function drawPassportOverlay(best, bestRect, bestBounds, bestArea, frameArea) {
  const ctx = passportOverlay.getContext("2d");
  ctx.clearRect(0, 0, passportOverlay.width, passportOverlay.height);
  drawPassportGuide();

  if (!best) {
    passportStableFrames = 0;
    passportLastTarget = null;
    updatePassportInstruction("Align the document inside the frame.");
    return;
  }

  let points = [];
  if (best.rows === 4) {
    for (let i = 0; i < 4; i++) {
      const x = best.data32S[i * 2];
      const y = best.data32S[i * 2 + 1];
      points.push({ x, y });
    }
  } else if (bestRect) {
    const rectPoints = cv.RotatedRect.points(bestRect);
    points = rectPoints.map((p) => ({ x: p.x, y: p.y }));
  } else if (bestBounds) {
    points = [
      { x: bestBounds.x, y: bestBounds.y },
      { x: bestBounds.x + bestBounds.width, y: bestBounds.y },
      { x: bestBounds.x + bestBounds.width, y: bestBounds.y + bestBounds.height },
      { x: bestBounds.x, y: bestBounds.y + bestBounds.height },
    ];
  } else {
    drawPassportGuide();
    passportStableFrames = 0;
    passportLastTarget = null;
    updatePassportInstruction("Align the document inside the frame.");
    return;
  }

  const center = points.reduce(
    (acc, p) => ({ x: acc.x + p.x / 4, y: acc.y + p.y / 4 }),
    { x: 0, y: 0 }
  );

  const areaRatio = bestArea / frameArea;
  const bounds = points.reduce(
    (acc, p) => ({
      minX: Math.min(acc.minX, p.x),
      maxX: Math.max(acc.maxX, p.x),
      minY: Math.min(acc.minY, p.y),
      maxY: Math.max(acc.maxY, p.y),
    }),
    { minX: points[0].x, maxX: points[0].x, minY: points[0].y, maxY: points[0].y }
  );
  const rectW = Math.max(1, bounds.maxX - bounds.minX);
  const rectH = Math.max(1, bounds.maxY - bounds.minY);
  const aspect = rectW > rectH ? rectW / rectH : rectH / rectW;
  const aspectOk = aspect >= 1.1 && aspect <= 2.1;
  const sizeOk = areaRatio > 0.02 && areaRatio < 0.95 && aspectOk;

  if (sizeOk && passportLastTarget) {
    const dx = center.x - passportLastTarget.x;
    const dy = center.y - passportLastTarget.y;
    const dist = Math.hypot(dx, dy);
    const areaDelta = Math.abs(bestArea - passportLastTarget.area) / passportLastTarget.area;
    if (dist < 30 && areaDelta < 0.25) {
      passportStableFrames += 1;
    } else {
      passportStableFrames = 0;
    }
  } else if (sizeOk) {
    passportStableFrames = 1;
  } else {
    passportStableFrames = 0;
  }

  passportLastTarget = { x: center.x, y: center.y, area: bestArea };

  const stableTarget = passportStableFrames >= 6 && sizeOk;
  ctx.strokeStyle = stableTarget ? "#2aa06f" : "#d5a14b";
  ctx.lineWidth = 3;
  ctx.beginPath();
  ctx.moveTo(points[0].x, points[0].y);
  points.slice(1).forEach((p) => ctx.lineTo(p.x, p.y));
  ctx.closePath();
  ctx.stroke();

  if (!aspectOk) {
    updatePassportInstruction("Rotate/align the document so it is horizontal.");
  } else if (!sizeOk) {
    updatePassportInstruction("Move the document closer and keep it centered.");
  } else if (stableTarget) {
    updatePassportInstruction("Hold steady... capturing.");
    triggerPassportCapture();
  } else {
    updatePassportInstruction("Hold steady for auto capture.");
  }
}

function drawPassportGuide() {
  const ctx = passportOverlay.getContext("2d");
  ctx.clearRect(0, 0, passportOverlay.width, passportOverlay.height);

  const width = passportOverlay.width;
  const height = passportOverlay.height;
  if (!width || !height) {
    return;
  }

  const guideWidth = width * 0.8;
  const guideHeight = height * 0.55;
  const x = (width - guideWidth) / 2;
  const y = (height - guideHeight) / 2;

  ctx.setLineDash([8, 6]);
  ctx.strokeStyle = "#c2b6a9";
  ctx.lineWidth = 2;
  ctx.strokeRect(x, y, guideWidth, guideHeight);
  ctx.setLineDash([]);
}

async function triggerPassportCapture() {
  if (passportCaptureInProgress || !passportFirstBase64) {
    return;
  }

  passportCaptureInProgress = true;
  passportStableFrames = 0;

  const width = passportOverlay.width;
  const height = passportOverlay.height;
  passportCaptureCtx.drawImage(passportCamera, 0, 0, width, height);
  const dataUrl = passportCaptureCanvas.toDataURL("image/jpeg", 0.92);
  const secondDocumentImageBase64 = dataUrl.split(",")[1];

  if (passportPreview2) {
    passportPreview2.src = dataUrl;
  }

  updatePassportInstruction("Captured. Comparing...");
  stopPassportCamera();
  await submitPassportCompare(passportFirstBase64, secondDocumentImageBase64);
}

async function submitPassportCompare(firstDocumentImageBase64, secondDocumentImageBase64) {
  const response = await fetch("/api/documents/compare-documents", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ firstDocumentImageBase64, secondDocumentImageBase64 }),
  });

  const data = await response.json();
  setResult(data);
  setPassportSummary(data);
  updatePassportInstruction("Done. Upload another document to compare again.");
}

function setMatchPreview(src1, src2) {
  if (src1) {
    matchPreview1.src = src1;
  }
  if (src2) {
    matchPreview2.src = src2;
  }
}

function setIcaoPreview(src) {
  if (!icaoPreview) {
    return;
  }

  icaoPreview.src = src || "";
}

function renderIcaoSummary(data) {
  if (!icaoSummary || !icaoSections) {
    return;
  }

  const sections = Array.isArray(data?.sections) ? data.sections : [];
  const total = data?.totalCount ?? null;
  const totalCompliant = data?.totalCompliantCount ?? null;
  const percent = data?.compliancePercent ?? null;

  if (!sections.length) {
    icaoSummary.hidden = true;
    icaoSections.hidden = true;
    icaoSections.innerHTML = "";
    if (icaoPercent) icaoPercent.textContent = "â€”";
    if (icaoTotals) icaoTotals.textContent = "â€”";
    return;
  }

  icaoSummary.hidden = false;
  icaoSections.hidden = false;

  if (icaoPercent) {
    icaoPercent.textContent = percent == null ? "â€”" : `${Number(percent).toFixed(2)}%`;
  }
  if (icaoTotals) {
    if (total != null && totalCompliant != null) {
      icaoTotals.textContent = `${totalCompliant}/${total}`;
    } else {
      icaoTotals.textContent = "â€”";
    }
  }

  icaoSections.innerHTML = "";
  sections.forEach((section) => {
    const name = section?.name ?? "Section";
    const compliant = section?.compliantCount ?? 0;
    const sectionTotal = section?.totalCount ?? 0;

    const row = document.createElement("div");
    row.className = "icao-section";

    const nameEl = document.createElement("span");
    nameEl.className = "name";
    nameEl.textContent = formatIcaoName(name);

    const countEl = document.createElement("span");
    countEl.className = "count";
    countEl.textContent = `${compliant}/${sectionTotal}`;

    row.appendChild(nameEl);
    row.appendChild(countEl);
    icaoSections.appendChild(row);
  });
}

function formatIcaoName(value) {
  if (!value) {
    return "Section";
  }

  const withSpaces = value.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/_/g, " ");
  return withSpaces.replace(/\s+/g, " ").trim();
}

function showMatchScore(data) {
  const similarity = data?.similarity ?? data?.Similarity ?? data?.score ?? data?.Score ?? null;
  if (similarity == null) {
    matchScore1.textContent = "—";
    matchScore2.textContent = "—";
    return;
  }

  const percent = similarity > 1 ? similarity : similarity * 100;
  const label = `${percent.toFixed(1)}% match`;
  matchScore1.textContent = label;
  matchScore2.textContent = label;
}

function setVerifyPreview(docSrc, liveSrc, similarity) {
  if (verifyPreviewDoc && docSrc) {
    verifyPreviewDoc.src = docSrc;
  }
  if (verifyPreviewLive && liveSrc) {
    verifyPreviewLive.src = liveSrc;
  }

  if (!verifyScoreDoc || !verifyScoreLive) {
    return;
  }

  if (similarity == null) {
    verifyScoreDoc.textContent = "—";
    verifyScoreLive.textContent = "—";
    return;
  }

  const percent = similarity > 1 ? similarity : similarity * 100;
  const label = `${percent.toFixed(1)}% match`;
  verifyScoreDoc.textContent = label;
  verifyScoreLive.textContent = label;
}

function setPassportSummary(data) {
  if (!passportSummary) {
    return;
  }

  const scorePercent = data?.faceMatchScorePercent ?? null;
  const score = data?.faceMatchScore ?? null;
  const threshold = data?.faceMatchThreshold ?? null;

  passportSummary.hidden = false;

  if (passportFaceScore) {
    if (scorePercent != null) {
      passportFaceScore.textContent = `${Number(scorePercent).toFixed(1)}%`;
    } else if (score != null) {
      const percent = score > 1 ? score * 100 : score * 100;
      passportFaceScore.textContent = `${Number(percent).toFixed(1)}%`;
    } else {
      passportFaceScore.textContent = "â€”";
    }
  }

  updateBadge(passportNumberMatch, data?.isDocumentNumberMatch);
  updateBadge(passportNameMatch, data?.isNameMatch);
  updateBadge(passportDobMatch, data?.isDobMatch);

  if (threshold != null && passportFaceScore && score != null) {
    const label = threshold >= 1 ? `${threshold}%` : `${(threshold * 100).toFixed(0)}%`;
    passportFaceScore.textContent = `${passportFaceScore.textContent} (threshold ${label})`;
  }
}

function updateBadge(element, isMatch) {
  if (!element) {
    return;
  }

  element.classList.remove("match", "mismatch");
  if (isMatch === true) {
    element.classList.add("match");
    element.textContent = `${element.textContent.split(":")[0]}: match`;
  } else if (isMatch === false) {
    element.classList.add("mismatch");
    element.textContent = `${element.textContent.split(":")[0]}: mismatch`;
  } else {
    element.textContent = element.textContent.split(":")[0];
  }
}

async function submitDocument(url) {
  const files = Array.from(docInput.files || []);
  if (files.length === 0) {
    setResult({ error: "Select document images or a PDF." });
    return;
  }

  const formData = new FormData();
  files.forEach((file) => formData.append("images", file));

  const scenario = docScenario.value.trim();
  if (scenario) {
    formData.append("scenario", scenario);
  }

  const response = await fetch(url, {
    method: "POST",
    body: formData,
  });

  const data = await response.json();
  setResult(data);
}

function renderFraudSummary(data) {
  if (!fraudSummary || !fraudChecks) {
    return;
  }

  const checks = Array.isArray(data?.checks) ? data.checks : [];
  if (!checks.length) {
    fraudSummary.hidden = true;
    fraudChecks.innerHTML = "";
    if (fraudNotApplicable) {
      fraudNotApplicable.hidden = true;
    }
    return;
  }

  fraudSummary.hidden = false;

  if (fraudTransaction) {
    fraudTransaction.textContent = data?.transactionId ?? data?.TransactionId ?? "—";
  }
  if (fraudOverall) {
    const overallValue = data?.overallStatus ?? data?.OverallStatus ?? null;
    fraudOverall.textContent = formatOverallStatus(overallValue);
  }

  fraudChecks.innerHTML = "";
  checks
    .filter((check) => normalizeStatus(check?.status) !== "na")
    .forEach((check) => {
    const item = document.createElement("div");
    item.className = "fraud-check";

    const name = document.createElement("div");
    name.className = "fraud-name";
    name.textContent = check?.name ?? "Check";

    const status = document.createElement("span");
    status.className = `fraud-status ${normalizeStatus(check?.status)}`;
    status.textContent = formatStatus(check?.status);

    const details = document.createElement("div");
    details.className = "fraud-details";
    details.textContent = check?.details ?? "";

    const header = document.createElement("div");
    header.className = "fraud-header";
    let help = null;
    const helpText = getFraudHelpText(check?.name);
    if (helpText) {
      const helpBtn = document.createElement("button");
      helpBtn.type = "button";
      helpBtn.className = "fraud-help-btn";
      helpBtn.setAttribute("aria-label", "Explain this check");
      helpBtn.setAttribute("title", "What does this check mean?");
      helpBtn.textContent = "?";

      help = document.createElement("div");
      help.className = "fraud-help";
      help.hidden = true;
      help.textContent = helpText;

      helpBtn.addEventListener("click", () => {
        const isHidden = help.hidden;
        help.hidden = !isHidden;
        helpBtn.setAttribute("aria-expanded", String(isHidden));
      });

      name.appendChild(helpBtn);
    }
    header.appendChild(name);
    header.appendChild(status);

    item.appendChild(header);
    if (help) {
      item.appendChild(help);
    }
    if (details.textContent) {
      item.appendChild(details);
    }

    fraudChecks.appendChild(item);
  });

  if (fraudNotApplicable && fraudNotApplicableList) {
    fraudNotApplicable.hidden = true;
    fraudNotApplicableList.innerHTML = "";
  }
}

function getFraudHelpText(name) {
  const key = String(name || "").trim().toLowerCase();
  const help = {
    "document type identification":
      "Checks whether the document matches a known template (type, issuer, and format).",
    "image quality assessment":
      "Evaluates image clarity, focus, lighting, and noise to ensure the document can be reliably analyzed.",
    "document liveness check":
      "Looks for signs that the document is a real physical document and not a recaptured image or screen.",
    "hologram / ovi / mli / dynaprint check":
      "Verifies optical security features like holograms, OVI/MLI inks, and dynamic print elements.",
    "mrz (machine readable zone) check":
      "Validates the MRZ text and checksum to confirm consistency and integrity.",
    "visual zone ocr validation":
      "Checks OCR results from the visual zone to ensure text is readable and consistent.",
    "photo embedding check":
      "Confirms the portrait area exists and matches expected placement/structure.",
    "security pattern / image pattern check":
      "Evaluates security patterns and background features to detect tampering.",
    "extended mrz & extended ocr":
      "Cross-compares MRZ data with extended OCR fields to detect mismatches.",
    "geometry check":
      "Verifies document position, angle, and perspective to ensure proper capture and reduce spoofing.",
    "data cross-validation":
      "Compares data across sources (MRZ, OCR, barcode, etc.) to confirm consistency."
  };

  return help[key] || "";
}

function normalizeStatus(status) {
  const value = String(status || "").toLowerCase();
  if (value === "pass" || value === "passed") return "pass";
  if (value === "fail" || value === "failed") return "fail";
  if (value === "not_applicable" || value === "not applicable" || value === "na") return "na";
  return "unknown";
}

function formatStatus(status) {
  const value = String(status || "").toLowerCase();
  if (value === "pass" || value === "passed") return "Pass";
  if (value === "fail" || value === "failed") return "Fail";
  if (value === "not_applicable" || value === "not applicable" || value === "na") return "N/A";
  if (!value) return "Unknown";
  return value.replace(/_/g, " ").replace(/\b\w/g, (c) => c.toUpperCase());
}

function formatOverallStatus(value) {
  if (value == null || value === "") {
    return "—";
  }

  const normalized = String(value).trim();
  const numeric = Number(normalized);
  const descriptions = {
    0: "0 (Fail)",
    1: "1 (Pass)",
    2: "2 (Not Completed)",
    3: "3 (Not Performed)",
  };

  if (!Number.isNaN(numeric) && Number.isFinite(numeric)) {
    return descriptions[numeric] ?? `${numeric} (Unknown)`;
  }

  const lower = normalized.toLowerCase();
  if (lower === "pass" || lower === "passed") return "Pass";
  if (lower === "fail" || lower === "failed") return "Fail";
  if (lower === "not available" || lower === "not_available" || lower === "na") return "N/A";
  if (lower === "not performed" || lower === "not_performed") return "Not Performed";

  return normalized;
}

