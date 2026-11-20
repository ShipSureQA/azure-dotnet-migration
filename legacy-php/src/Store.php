<?php
declare(strict_types=1);

/**
 * File-backed store. `php -S` starts a fresh PHP process state per request,
 * so the legacy app persists its data in a JSON file (like the original
 * "shared hosting" deployment did).
 */
final class Store
{
    private string $file;
    private string $seedFile;

    public function __construct(string $file, string $seedFile)
    {
        $this->file = $file;
        $this->seedFile = $seedFile;
        if (!is_file($this->file)) {
            $this->reset();
        }
    }

    public function reset(): void
    {
        copy($this->seedFile, $this->file);
    }

    public function read(): array
    {
        $raw = file_get_contents($this->file);
        return json_decode((string) $raw, true, 512, JSON_THROW_ON_ERROR);
    }

    /** Runs $fn with an exclusive lock and saves the data it returns. */
    public function update(callable $fn): mixed
    {
        $handle = fopen($this->file, 'c+');
        flock($handle, LOCK_EX);
        $data = json_decode((string) stream_get_contents($handle), true, 512, JSON_THROW_ON_ERROR);
        [$data, $result] = $fn($data);
        ftruncate($handle, 0);
        rewind($handle);
        fwrite($handle, json_encode($data, JSON_PRETTY_PRINT | JSON_UNESCAPED_SLASHES));
        fflush($handle);
        flock($handle, LOCK_UN);
        fclose($handle);
        return $result;
    }
}
