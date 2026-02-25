const uploadInput = document.getElementById("uploadInput");
const uploadBtn = document.getElementById("uploadBtn");
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

let stream = null;

const setResult = (data) => {
  resultJson.textContent = JSON.stringify(data, null, 2);
};

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
