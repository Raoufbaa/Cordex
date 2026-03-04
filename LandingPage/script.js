// Fetch download count directly from GitHub API
async function loadDownloadCount() {
  try {
    const apiResponse = await fetch(
      "https://api.github.com/repos/Raoufbaa/Cordex/releases",
    );
    const releases = await apiResponse.json();

    let totalDownloads = 0;

    for (const release of releases) {
      if (release.assets && Array.isArray(release.assets)) {
        for (const asset of release.assets) {
          totalDownloads += asset.download_count || 0;
        }
      }
    }

    document.getElementById("downloadCount").textContent =
      totalDownloads.toLocaleString();
    const counter2 = document.getElementById("downloadCount2");
    if (counter2) {
      counter2.textContent = totalDownloads.toLocaleString();
    }
  } catch (error) {
    console.error("Error fetching download count:", error);
    document.getElementById("downloadCount").textContent = "N/A";
    const counter2 = document.getElementById("downloadCount2");
    if (counter2) {
      counter2.textContent = "N/A";
    }
  }
}

loadDownloadCount();
setInterval(loadDownloadCount, 5 * 60 * 1000);

function generateSquares() {
  const container = document.getElementById("gridSquares");
  const squareSize = 80;
  const cols = Math.ceil(window.innerWidth / squareSize) + 1;
  const rows = Math.ceil(window.innerHeight / squareSize) + 1;

  for (let row = 0; row < rows; row++) {
    for (let col = 0; col < cols; col++) {
      const square = document.createElement("div");
      square.className = "square";
      square.style.left = `${col * squareSize}px`;
      square.style.top = `${row * squareSize}px`;
      container.appendChild(square);
    }
  }
}

let lineGrid = { horizontal: [], vertical: [] };

function generateLines() {
  const container = document.getElementById("gridLines");
  const squareSize = 80;
  const cols = Math.ceil(window.innerWidth / squareSize) + 1;
  const rows = Math.ceil(window.innerHeight / squareSize) + 1;

  lineGrid = { horizontal: [], vertical: [] };

  for (let row = 0; row <= rows; row++) {
    lineGrid.horizontal[row] = [];
    for (let col = 0; col < cols; col++) {
      const line = document.createElement("div");
      line.className = "grid-line horizontal";
      line.style.left = `${col * squareSize}px`;
      line.style.top = `${row * squareSize}px`;
      container.appendChild(line);
      lineGrid.horizontal[row][col] = line;
    }
  }

  for (let col = 0; col <= cols; col++) {
    lineGrid.vertical[col] = [];
    for (let row = 0; row < rows; row++) {
      const line = document.createElement("div");
      line.className = "grid-line vertical";
      line.style.left = `${col * squareSize}px`;
      line.style.top = `${row * squareSize}px`;
      container.appendChild(line);
      lineGrid.vertical[col][row] = line;
    }
  }

  for (let i = 0; i < 30; i++) {
    setTimeout(() => animateSnake(), i * 300);
  }
}

function animateSnake() {
  const path = [];
  const rows = lineGrid.horizontal.length;
  const cols = lineGrid.vertical.length;

  let isHorizontal = Math.random() > 0.5;
  let row = Math.floor(Math.random() * rows);
  let col = Math.floor(Math.random() * cols);

  const visited = new Set();
  const maxLength = 25 + Math.floor(Math.random() * 25);

  while (path.length < maxLength) {
    const key = `${isHorizontal ? "h" : "v"}-${row}-${col}`;
    if (visited.has(key)) break;
    visited.add(key);

    if (
      isHorizontal &&
      lineGrid.horizontal[row] &&
      lineGrid.horizontal[row][col]
    ) {
      path.push(lineGrid.horizontal[row][col]);
    } else if (
      !isHorizontal &&
      lineGrid.vertical[col] &&
      lineGrid.vertical[col][row]
    ) {
      path.push(lineGrid.vertical[col][row]);
    } else {
      break;
    }

    const directions = [];

    if (isHorizontal) {
      if (lineGrid.vertical[col + 1]) {
        if (row > 0)
          directions.push({ isHorizontal: false, row: row - 1, col: col + 1 });
        if (row < rows - 1)
          directions.push({ isHorizontal: false, row: row, col: col + 1 });
      }
      if (col < cols - 1 && lineGrid.horizontal[row]) {
        directions.push({ isHorizontal: true, row: row, col: col + 1 });
      }
    } else {
      if (lineGrid.horizontal[row + 1]) {
        if (col > 0)
          directions.push({ isHorizontal: true, row: row + 1, col: col - 1 });
        if (col < cols - 1)
          directions.push({ isHorizontal: true, row: row + 1, col: col });
      }
      if (row < rows - 1 && lineGrid.vertical[col]) {
        directions.push({ isHorizontal: false, row: row + 1, col: col });
      }
    }

    if (directions.length === 0) break;

    const next = directions[Math.floor(Math.random() * directions.length)];
    isHorizontal = next.isHorizontal;
    row = next.row;
    col = next.col;
  }

  let currentIndex = 0;
  const snakeLength = 12;

  function moveSnake() {
    if (currentIndex < path.length) {
      path[currentIndex].classList.add("active");
    }
    const tailIndex = currentIndex - snakeLength;
    if (tailIndex >= 0) {
      path[tailIndex].classList.remove("active");
    }
    currentIndex++;
    if (currentIndex < path.length + snakeLength) {
      setTimeout(moveSnake, 50);
    } else {
      setTimeout(() => animateSnake(), Math.random() * 4000 + 2000);
    }
  }

  moveSnake();
}

generateSquares();
generateLines();

let resizeTimeout;
window.addEventListener("resize", () => {
  clearTimeout(resizeTimeout);
  resizeTimeout = setTimeout(() => {
    document.getElementById("gridSquares").innerHTML = "";
    document.getElementById("gridLines").innerHTML = "";
    generateSquares();
    generateLines();
  }, 250);
});

const observerOptions = {
  threshold: 0.2,
  rootMargin: "0px",
};

const observer = new IntersectionObserver((entries) => {
  entries.forEach((entry) => {
    if (entry.isIntersecting) {
      entry.target.classList.add("visible");
      const sectionIndex = entry.target.getAttribute("data-section");
      updateProgressDots(sectionIndex);
    }
  });
}, observerOptions);

document.querySelectorAll(".section").forEach((section) => {
  observer.observe(section);
});

function updateProgressDots(activeIndex) {
  const dots = document.querySelectorAll(".progress-dot");
  dots.forEach((dot, index) => {
    if (index <= activeIndex) {
      dot.classList.add("active");
    } else {
      dot.classList.remove("active");
    }
  });
}

document.querySelectorAll(".progress-dot").forEach((dot) => {
  dot.addEventListener("click", () => {
    const sectionIndex = dot.getAttribute("data-section");
    const section = document.querySelector(`[data-section="${sectionIndex}"]`);
    section.scrollIntoView({ behavior: "smooth" });
  });
});

// Hero Image Slider with 3D Tilt Effect
const heroImages = document.querySelectorAll(".heroimg");
const heroImageContainer = document.querySelector(".hero-image-container");
let currentImageIndex = 0;
let sliderInterval;
let isHovering = false;

function showNextImage() {
  if (isHovering) return;
  heroImages[currentImageIndex].classList.remove("active");
  currentImageIndex = (currentImageIndex + 1) % heroImages.length;
  heroImages[currentImageIndex].classList.add("active");
}

function startSlider() {
  sliderInterval = setInterval(showNextImage, 5000);
}

function stopSlider() {
  clearInterval(sliderInterval);
}

if (heroImages.length > 0 && heroImageContainer) {
  startSlider();

  heroImageContainer.addEventListener("mouseenter", () => {
    isHovering = true;
    stopSlider();
  });

  heroImageContainer.addEventListener("mouseleave", () => {
    isHovering = false;
    startSlider();
  });

  heroImageContainer.addEventListener("mousemove", (e) => {
    const rect = heroImageContainer.getBoundingClientRect();
    const x = e.clientX - rect.left;
    const y = e.clientY - rect.top;
    const centerX = rect.width / 2;
    const centerY = rect.height / 2;
    const rotateX = ((y - centerY) / centerY) * -8;
    const rotateY = ((x - centerX) / centerX) * 8;
    const activeImg = heroImages[currentImageIndex];
    activeImg.style.transform = `perspective(1000px) rotateX(${rotateX}deg) rotateY(${rotateY}deg) scale3d(1.01, 1.01, 1.01)`;
  });

  heroImageContainer.addEventListener("mouseleave", () => {
    const activeImg = heroImages[currentImageIndex];
    activeImg.style.transform =
      "perspective(1000px) rotateX(0deg) rotateY(0deg) scale3d(1, 1, 1)";
  });
}
