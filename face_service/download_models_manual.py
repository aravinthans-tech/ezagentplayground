"""
Manual model downloader for InsightFace 0.2.1
Downloads models from InsightFace GitHub releases
"""
import os
import urllib.request
import zipfile
import shutil

def download_buffalo_l():
    """Download buffalo_l model manually."""
    model_root = os.path.expanduser('~/.insightface/models')
    model_name = 'buffalo_l'
    model_path = os.path.join(model_root, model_name)
    
    os.makedirs(model_path, exist_ok=True)
    
    # Model URLs from InsightFace releases
    # Note: These URLs may need to be updated
    model_urls = {
        'detection': 'https://github.com/deepinsight/insightface/releases/download/v0.7/buffalo_l.zip',
        # Alternative: direct download links if available
    }
    
    print(f"Downloading {model_name} model...")
    print("Note: For InsightFace 0.2.1, you may need to:")
    print("1. Visit: https://github.com/deepinsight/insightface")
    print("2. Download models manually")
    print("3. Extract to: ~/.insightface/models/buffalo_l/")
    print(f"\nExpected location: {model_path}")
    
    # Try to download from a known source
    try:
        url = 'https://github.com/deepinsight/insightface/releases/download/v0.7/buffalo_l.zip'
        zip_path = os.path.join(model_root, 'buffalo_l.zip')
        
        print(f"\nAttempting to download from: {url}")
        print("This may take several minutes...")
        
        urllib.request.urlretrieve(url, zip_path)
        print("Download complete, extracting...")
        
        with zipfile.ZipFile(zip_path, 'r') as zip_ref:
            zip_ref.extractall(model_path)
        
        os.remove(zip_path)
        print(f"Model extracted to: {model_path}")
        
        # Check for ONNX files
        import glob
        onnx_files = glob.glob(os.path.join(model_path, '*.onnx'))
        print(f"Found {len(onnx_files)} ONNX file(s)")
        
    except Exception as e:
        print(f"Automatic download failed: {e}")
        print("\nPlease download models manually:")
        print("1. Go to: https://github.com/deepinsight/insightface")
        print("2. Download the buffalo_l model")
        print(f"3. Extract to: {model_path}")

if __name__ == '__main__':
    download_buffalo_l()

