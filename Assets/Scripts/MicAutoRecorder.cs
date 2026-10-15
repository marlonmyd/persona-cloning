using UnityEngine;
using UnityEngine.UI;
using System.IO;
using System.Collections.Generic;
using TMPro;
using System;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class MicAutoRecorder : MonoBehaviour
{
    [Header("UI References")]
    public TMP_Dropdown micDropdown;
    public Image levelBarImage;

    [Header("Recording & Save Settings")]
    [Tooltip("The folder where recordings will be saved.")]
    [SerializeField] private string saveFolderPath = "";

    [Range(0.0001f, 0.5f)]
    public float threshold = 0.02f; 
    public float maxRecordTime = 60f;

    [Header("Silence Auto-Stop Settings")]
    [Tooltip("Seconds of silence before auto-stopping and saving.")]
    public float silenceDurationToStop = 2.0f;
    private float silenceTimer = 0f;

    [Header("Debug Info")]
    [SerializeField] private float currentMicLevel = 0f;
    [SerializeField] private bool isCurrentlyRecording = false;

    private string[] micDevices;
    private string currentMic;
    private AudioClip micClip;
    private bool isRecording = false;
    private int sampleRate = 44100;

    void Start()
    {
        if (string.IsNullOrEmpty(saveFolderPath))
        {
            saveFolderPath = Application.persistentDataPath;
        }

        Microphone.GetDeviceCaps(null, out int minFreq, out int maxFreq);
        if (maxFreq >= 44100) sampleRate = 44100;
        else if (maxFreq >= 22050) sampleRate = 22050;
        else sampleRate = maxFreq > 0 ? maxFreq : 16000;

        InitializeMicrophones();
        
        if (!string.IsNullOrEmpty(currentMic))
        {
            micClip = Microphone.Start(currentMic, true, 1, sampleRate);
            isCurrentlyRecording = true;
        }
    }

    void InitializeMicrophones()
    {
        micDevices = Microphone.devices;
        if (micDevices.Length > 0)
        {
            if (micDropdown != null)
            {
                micDropdown.ClearOptions();
                List<string> options = new List<string>(micDevices);
                micDropdown.AddOptions(options);
                micDropdown.onValueChanged.AddListener(OnMicChanged);
                
                currentMic = micDevices[0];
                micDropdown.value = 0;
            }
            Debug.Log("Microphones detected: " + micDevices.Length);
        }
        else
        {
            Debug.LogError("No microphone found on your PC!");
        }
    }

    void OnMicChanged(int index)
    {
        if (index >= 0 && index < micDevices.Length)
        {
            if (isCurrentlyRecording)
            {
                Microphone.End(currentMic);
            }
            
            currentMic = micDevices[index];
            Debug.Log("Switched microphone to: " + currentMic);

            micClip = Microphone.Start(currentMic, true, 1, sampleRate);
            isCurrentlyRecording = true;
        }
    }

    void Update()
    {
        if (micDevices == null || micDevices.Length == 0 || micClip == null) return;

        currentMicLevel = GetMicVolume();
        
        if (levelBarImage != null)
        {
            levelBarImage.fillAmount = Mathf.Clamp01(currentMicLevel * 15f);
        }

        if (!isRecording)
        {
            if (currentMicLevel > threshold)
            {
                StartActualRecording();
            }
        }
        else
        {
            if (currentMicLevel < threshold)
            {
                silenceTimer += Time.deltaTime;
                if (silenceTimer >= silenceDurationToStop)
                {
                    Debug.Log(">> Speech ended (Silence detected). Auto-stopping and saving...");
                    StopAndSaveRecording();
                }
            }
            else
            {
                silenceTimer = 0f;
            }

            if (!Microphone.IsRecording(currentMic))
            {
                StopAndSaveRecording();
            }
        }
    }

    float GetMicVolume()
    {
        int micPosition = Microphone.GetPosition(currentMic) - 128;
        if (micPosition < 0) return 0f;

        float[] waveData = new float[128];
        micClip.GetData(waveData, micPosition);
        
        float totalSum = 0;
        foreach (float sample in waveData)
        {
            totalSum += Mathf.Abs(sample);
        }
        return totalSum / 128f;
    }

    void StartActualRecording()
    {
        Microphone.End(currentMic);
        
        micClip = Microphone.Start(currentMic, false, (int)maxRecordTime, sampleRate);
        isRecording = true;
        silenceTimer = 0f;
        Debug.Log(">> Started recording because volume exceeded threshold! Level: " + currentMicLevel);
    }

    public void StopAndSaveRecording()
    {
        if (isRecording)
        {
            int recordedSamples = Microphone.GetPosition(currentMic);
            Microphone.End(currentMic);
            isRecording = false;

            SaveAudioFile(recordedSamples);

            micClip = Microphone.Start(currentMic, true, 1, sampleRate);
            isCurrentlyRecording = true;
        }
    }

    void SaveAudioFile(int recordedSamples)
    {
        if (!Directory.Exists(saveFolderPath))
        {
            Directory.CreateDirectory(saveFolderPath);
        }

        string fileName = "Audio_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string path = Path.Combine(saveFolderPath, fileName + ".wav");
        
        SaveWavToFile(path, micClip, recordedSamples);
        Debug.Log("WAV file successfully saved at: " + path);
    }

    bool SaveWavToFile(string filepath, AudioClip clip, int samples)
    {
        if (clip == null) return false;

        int hz = clip.frequency;
        int channels = clip.channels;
        int sampleCount = samples > 0 && samples < clip.samples * channels ? samples : clip.samples * channels;

        using (FileStream fileStream = CreateWavFile(filepath, channels, hz, sampleCount, out BinaryWriter writer))
        {
            if (fileStream == null) return false;

            float[] data = new float[sampleCount];
            clip.GetData(data, 0);

            foreach (var sample in data)
            {
                short val = (short)(Mathf.Clamp(sample, -1f, 1f) * 32767);
                writer.Write(val);
            }

            writer.Close();
            fileStream.Close();
        }
        return true;
    }

    FileStream CreateWavFile(string filepath, int channels, int hz, int sampleCount, out BinaryWriter writer)
    {
        try
        {
            var fileStream = new FileStream(filepath, FileMode.Create);
            writer = new BinaryWriter(fileStream);

            int subChunk2Size = sampleCount * 2;
            int chunkSize = 36 + subChunk2Size;
            int byteRate = hz * channels * 2;
            ushort blockAlign = (ushort)(channels * 2);

            string chunkID = "RIFF";
            writer.Write(chunkID.ToCharArray());
            writer.Write(chunkSize); 
            string format = "WAVE";
            writer.Write(format.ToCharArray());
            string subchunk1ID = "fmt ";
            writer.Write(subchunk1ID.ToCharArray());
            writer.Write(16); 
            writer.Write((ushort)1); 
            writer.Write((ushort)channels); 
            writer.Write(hz); 
            writer.Write(byteRate); 
            writer.Write(blockAlign); 
            writer.Write((ushort)16); 
            string subchunk2ID = "data";
            writer.Write(subchunk2ID.ToCharArray());
            writer.Write(subChunk2Size); 

            return fileStream;
        }
        catch (Exception ex)
        {
            Debug.LogError("Error creating WAV file: " + ex.Message);
            writer = null;
            return null;
        }
    }

    public void BrowseFolder()
    {
#if UNITY_EDITOR
        string selectedPath = EditorUtility.OpenFolderPanel("Select Save Folder", "", "");
        if (!string.IsNullOrEmpty(selectedPath))
        {
            saveFolderPath = selectedPath;
            Debug.Log("New Save Folder: " + saveFolderPath);
        }
#endif
    }

    void OnDisable()
    {
        if (micDevices != null && micDevices.Length > 0)
        {
            Microphone.End(currentMic);
        }
    }
}