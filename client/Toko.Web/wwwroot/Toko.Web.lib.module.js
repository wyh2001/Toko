export function beforeWebAssemblyStart(options) {
    // Create loading screen container
    const loadingScreen = document.createElement('div');
    loadingScreen.id = 'toko-loading-screen';
    loadingScreen.innerHTML = `
        <div class="toko-loading-container">
            <div class="toko-loading-logo">
                <h1 class="toko-loading-title">Toko Toko</h1>
            </div>
            <div class="toko-loading-progress">
                <div class="toko-progress-bar">
                    <div class="toko-progress-fill"></div>
                </div>
                <div class="toko-progress-text">Loading... 0%</div>
            </div>
        </div>
    `;
    
    // Add loading screen styles
    const style = document.createElement('style');
    style.textContent = `
        #toko-loading-screen {
            position: fixed;
            top: 0;
            left: 0;
            width: 100%;
            height: 100%;
            background: #ffffff;
            z-index: 9999;
            display: flex;
            justify-content: center;
            align-items: center;
            font-family: 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', 'Roboto', 'Oxygen', 'Ubuntu', 'Cantarell', 'Fira Sans', 'Droid Sans', 'Helvetica Neue', sans-serif;
        }
        
        .toko-loading-container {
            display: flex;
            flex-direction: column;
            align-items: center;
            gap: 3rem;
        }
        
        .toko-loading-logo {
            text-align: center;
        }
        
        .toko-loading-title {
            font-size: 4rem;
            font-weight: 900;
            color: #000000;
            letter-spacing: -2px;
            line-height: 0.75;
            margin: 0;
            position: relative;
        }
        
        .toko-loading-title::after {
            content: '';
            position: absolute;
            bottom: -12px;
            left: 0;
            width: 120px;
            height: 6px;
            background: #000000;
            clip-path: polygon(0 0, calc(100% - 16px) 0, 100% 100%, 16px 100%);
        }
        
        .toko-loading-progress {
            display: flex;
            flex-direction: column;
            align-items: center;
            gap: 1rem;
        }
        
        .toko-progress-bar {
            width: 300px;
            height: 6px;
            background: #f0f0f0;
            border: 2px solid #000000;
            position: relative;
            overflow: hidden;
        }
        
        .toko-progress-fill {
            height: 100%;
            background: #000000;
            width: 0%;
            transition: width 0.3s ease;
        }
        
        .toko-progress-text {
            font-size: 0.9rem;
            font-weight: 600;
            color: #666666;
            text-transform: uppercase;
            letter-spacing: 0.05em;
        }
        
        /* Responsive design */
        @media (max-width: 768px) {
            .toko-loading-title {
                font-size: 3rem;
                letter-spacing: -1px;
            }
            
            .toko-loading-title::after {
                width: 90px;
                height: 4px;
                bottom: -8px;
            }
            
            .toko-progress-bar {
                width: 250px;
            }
        }
        
        @media (max-width: 480px) {
            .toko-loading-title {
                font-size: 2.5rem;
            }
            
            .toko-loading-title::after {
                width: 75px;
                height: 3px;
                bottom: -6px;
            }
            
            .toko-progress-bar {
                width: 200px;
            }
        }
    `;
    
    // Add styles and loading screen to document
    document.head.appendChild(style);
    document.body.appendChild(loadingScreen);
    
    // Simulate loading progress
    let progress = 0;
    const progressFill = loadingScreen.querySelector('.toko-progress-fill');
    const progressText = loadingScreen.querySelector('.toko-progress-text');
    
    const updateProgress = (percentage) => {
        progress = Math.min(percentage, 100);
        progressFill.style.width = progress + '%';
        progressText.textContent = `Loading... ${Math.floor(progress)}%`;
    };
    
    // Start with some initial progress
    updateProgress(10);
    
    // Store progress updater for potential use
    window.tokoUpdateProgress = updateProgress;
    
    // Simulate realistic loading progression
    const progressInterval = setInterval(() => {
        if (progress < 90) {
            updateProgress(progress + Math.random() * 15);
        } else {
            clearInterval(progressInterval);
        }
    }, 100);
    
    // Store interval for cleanup
    window.tokoProgressInterval = progressInterval;
}

export function afterWebAssemblyStarted(blazor) {
    // Clean up progress interval
    if (window.tokoProgressInterval) {
        clearInterval(window.tokoProgressInterval);
    }
    
    // Final progress update
    if (window.tokoUpdateProgress) {
        window.tokoUpdateProgress(100);
    }
    
    // Remove loading screen after a short delay
    setTimeout(() => {
        const loadingScreen = document.getElementById('toko-loading-screen');
        if (loadingScreen) {
            loadingScreen.style.opacity = '0';
            loadingScreen.style.transition = 'opacity 0.3s ease';
            setTimeout(() => {
                loadingScreen.remove();
            }, 300);
        }
        
        // Clean up global variables
        delete window.tokoUpdateProgress;
        delete window.tokoProgressInterval;
    }, 200);
}
