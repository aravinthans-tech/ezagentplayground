"""
Helper script to download InsightFace models manually.
Run this once to download models before starting the service.
"""
import insightface.model_zoo.model_zoo as model_zoo
import os
import sys

def download_model(model_name='buffalo_l'):
    """Download InsightFace model."""
    model_root = os.path.expanduser('~/.insightface/models')
    os.makedirs(model_root, exist_ok=True)
    
    print(f"Downloading {model_name} model to {model_root}...")
    print("This may take several minutes...")
    
    try:
        # Try to get/download the model
        result = model_zoo.get_model(model_name, download=True, root=model_root)
        print(f"Download completed. Result: {result}")
        
        # Check if files were downloaded
        model_path = os.path.join(model_root, model_name)
        if os.path.exists(model_path):
            import glob
            onnx_files = glob.glob(os.path.join(model_path, '*.onnx'))
            print(f"Found {len(onnx_files)} ONNX file(s) in {model_path}")
            for f in onnx_files:
                print(f"  - {os.path.basename(f)}")
        else:
            print(f"Warning: Model directory {model_path} not found")
            
    except Exception as e:
        print(f"Error downloading model: {e}")
        print("\nTrying alternative download method...")
        # Try using insightface command if available
        try:
            import subprocess
            result = subprocess.run(
                [sys.executable, '-m', 'insightface', 'download-model', model_name],
                capture_output=True,
                text=True
            )
            print(f"CLI result: {result.stdout}")
            if result.stderr:
                print(f"CLI errors: {result.stderr}")
        except Exception as e2:
            print(f"Alternative method also failed: {e2}")

if __name__ == '__main__':
    download_model('buffalo_l')

