(() => {
  const tabs = Array.from(document.querySelectorAll(".stepper-tab"));
  const panels = Array.from(document.querySelectorAll(".stepper-panel"));
  const step2Next = document.getElementById("step2Next");
  const video = document.getElementById("trainingVideo");
  const playPauseBtn = document.getElementById("playPauseBtn");
  const videoStatus = document.getElementById("videoStatus");

  let currentStep = 1;
  let videoCompleted = false;
  let maxPlayedTime = 0;

  const formatRemaining = (seconds) => {
    if (!Number.isFinite(seconds) || seconds < 0) return "--:--";
    const total = Math.ceil(seconds);
    const minutes = Math.floor(total / 60);
    const secs = total % 60;
    return `${minutes}:${secs.toString().padStart(2, "0")}`;
  };

  const updateRemaining = () => {
    if (!videoStatus || !video) return;
    const remaining = (video.duration || 0) - (video.currentTime || 0);
    videoStatus.textContent = `Remaining: ${formatRemaining(remaining)}`;
  };

  const setActiveStep = (step) => {
    currentStep = step;
    tabs.forEach((tab) => {
      const isActive = Number(tab.dataset.step) === step;
      tab.classList.toggle("is-active", isActive);
    });
    panels.forEach((panel) => {
      const isActive = Number(panel.dataset.step) === step;
      panel.classList.toggle("is-active", isActive);
    });
  };

  const canNavigateTo = (step) => {
    if (step <= 2) return true;
    return videoCompleted;
  };

  const updateStep3Access = () => {
    const step3Tab = tabs.find((tab) => Number(tab.dataset.step) === 3);
    if (!step3Tab) return;
    step3Tab.disabled = !videoCompleted;
  };

  document.addEventListener("click", (event) => {
    const action = event.target?.dataset?.action;
    if (!action) return;

    if (action === "next") {
      const nextStep = Math.min(3, currentStep + 1);
      if (!canNavigateTo(nextStep)) return;
      setActiveStep(nextStep);
    }

    if (action === "prev") {
      const prevStep = Math.max(1, currentStep - 1);
      setActiveStep(prevStep);
    }
  });

  tabs.forEach((tab) => {
    tab.addEventListener("click", () => {
      const step = Number(tab.dataset.step);
      if (!canNavigateTo(step)) return;
      setActiveStep(step);
    });
  });

  if (video) {
    video.controls = false;
    video.controlsList = "nodownload noplaybackrate noremoteplayback";
    video.disablePictureInPicture = true;
    video.disableRemotePlayback = true;

    video.addEventListener("contextmenu", (event) => event.preventDefault());

    video.addEventListener("ratechange", () => {
      if (video.playbackRate !== 1) {
        video.playbackRate = 1;
      }
    });

    video.addEventListener("timeupdate", () => {
      if (!video.seeking) {
        maxPlayedTime = Math.max(maxPlayedTime, video.currentTime);
      }
      updateRemaining();
    });

    video.addEventListener("seeking", () => {
      if (video.currentTime > maxPlayedTime + 0.25) {
        video.currentTime = maxPlayedTime;
      }
    });

    video.addEventListener("ended", () => {
      videoCompleted = true;
      if (step2Next) step2Next.disabled = false;
      updateStep3Access();
      if (videoStatus) videoStatus.textContent = "Remaining: 0:00";
    });

    if (playPauseBtn) {
      playPauseBtn.addEventListener("click", async () => {
        if (video.paused) {
          await video.play();
        } else {
          video.pause();
        }
      });
    }

    video.addEventListener("play", () => {
      updateRemaining();
      if (playPauseBtn) playPauseBtn.textContent = "Pause";
    });

    video.addEventListener("pause", () => {
      if (!videoCompleted) updateRemaining();
      if (playPauseBtn) playPauseBtn.textContent = "Play";
    });

    video.addEventListener("loadedmetadata", updateRemaining);
  }

  updateStep3Access();
  setActiveStep(1);
})();
