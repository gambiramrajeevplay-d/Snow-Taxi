
using TMPro;
using UnityEngine;

public class TimeManager : MonoBehaviour
{
    [Header("Timer Settings")]
    [SerializeField] private float levelTime = 120f;

    private TMP_Text timerText;
    private float currentTime;
    private bool timerRunning;

    private void Start()
    {
        // Find Timer Text using tag
        GameObject timerObject = GameObject.FindGameObjectWithTag("TimerText");

        if (timerObject != null)
        {
            timerText = timerObject.GetComponent<TMP_Text>();
        }
        else
        {
            Debug.LogError("TimerText tag not found!");
        }

        currentTime = levelTime;
        timerRunning = true;

        UpdateTimerUI();
    }

    private void Update()
    {
        if (!timerRunning)
            return;

        if (GameManager.Instance != null &&
            GameManager.Instance.IsGameFinished)
        {
            timerRunning = false;
            return;
        }

        if (currentTime > 0f)
        {
            currentTime -= Time.deltaTime;

            if (currentTime <= 0f)
            {
                currentTime = 0f;
                timerRunning = false;

                UpdateTimerUI();
                TimeEnded();
            }

            UpdateTimerUI();
        }
    }

    private void TimeEnded()
    {
        Debug.Log("Time Over! Level Failed.");

        if (GameManager.Instance != null)
        {
            GameManager.Instance.LoseGame();
        }
        else
        {
            Debug.LogError("GameManager Instance not found!");
        }
    }

    private void UpdateTimerUI()
    {
        if (timerText == null)
            return;

        int minutes = Mathf.FloorToInt(currentTime / 60f);
        int seconds = Mathf.FloorToInt(currentTime % 60f);

        timerText.text = string.Format("{0:00}:{1:00}",
            minutes, seconds);
    }

    public void StopTimer()
    {
        timerRunning = false;
    }

    public void ResumeTimer()
    {
        if (currentTime > 0f)
            timerRunning = true;
    }

    public void ResetTimer()
    {
        currentTime = levelTime;
        timerRunning = true;

        UpdateTimerUI();
    }
}
