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

let stream = null;
let capturedPassportBase64 = null;

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

if (passportUploadInput) {
  passportUploadInput.addEventListener("change", async () => {
    const file = passportUploadInput.files[0];
    if (!file) {
      return;
    }
    const preview = await fileToDataUrl(file);
    if (passportPreview1) {
      passportPreview1.src = preview;
    }
  });
}

if (comparePassportsBtn) {
  comparePassportsBtn.addEventListener("click", async () => {
    const file = passportUploadInput?.files?.[0];
    if (!file) {
      setResult({ error: "Upload the first document image." });
      return;
    }

    if (!capturedPassportBase64) {
      if (!stream) {
        setResult({ error: "Start the webcam to capture the second document image." });
        return;
      }
      const frame = captureFrame();
      if (!frame) {
        setResult({ error: "Unable to capture the document frame." });
        return;
      }
      capturedPassportBase64 = frame;
      if (passportPreview2) {
        passportPreview2.src = base64ToDataUrl(frame);
      }
    }

    const firstDocumentImageBase64 = await fileToBase64(file);
    const secondDocumentImageBase64 = capturedPassportBase64;

    const response = await fetch("/api/documents/compare-documents", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ firstDocumentImageBase64, secondDocumentImageBase64 }),
    });

    const data = await response.json();
    setResult(data);
    setPassportSummary(data);
    capturedPassportBase64 = null;
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
    fraudOverall.textContent = data?.overallStatus ?? data?.OverallStatus ?? "—";
  }

  fraudChecks.innerHTML = "";
  checks.forEach((check) => {
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
    header.appendChild(name);
    header.appendChild(status);

    item.appendChild(header);
    if (details.textContent) {
      item.appendChild(details);
    }

    fraudChecks.appendChild(item);
  });

  const naList = Array.isArray(data?.notApplicable) ? data.notApplicable : [];
  if (fraudNotApplicable && fraudNotApplicableList) {
    if (!naList.length) {
      fraudNotApplicable.hidden = true;
      fraudNotApplicableList.innerHTML = "";
    } else {
      fraudNotApplicable.hidden = false;
      fraudNotApplicableList.innerHTML = "";
      naList.forEach((name) => {
        const pill = document.createElement("span");
        pill.className = "fraud-pill";
        pill.textContent = name;
        fraudNotApplicableList.appendChild(pill);
      });
    }
  }
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
