//! Non-blocking consumer for bounded decoded-audio chunks.
use std::{
    collections::VecDeque,
    sync::{
        Arc,
        atomic::AtomicBool,
        mpsc::{Receiver, TryRecvError},
    },
};

pub(super) struct StreamChunk {
    pub(super) rate: u32,
    pub(super) samples: Vec<f32>,
}

pub(super) struct StreamingSamples {
    pub(super) receiver: Receiver<StreamChunk>,
    pub(super) looping: Arc<AtomicBool>,
    pub(super) queued: VecDeque<f32>,
    pub(super) rate: u32,
    pub(super) ended: bool,
}

impl StreamingSamples {
    pub(super) fn fill(&mut self, required: usize) {
        while self.queued.len() < required && !self.ended {
            match self.receiver.try_recv() {
                Ok(chunk) => {
                    self.rate = chunk.rate;
                    self.queued.extend(chunk.samples);
                }
                Err(TryRecvError::Empty) => break,
                Err(TryRecvError::Disconnected) => self.ended = true,
            }
        }
    }

    pub(super) fn sample(&mut self, position: &mut f64, step: f64) -> Option<f32> {
        let i = *position as usize;
        self.fill(i.saturating_add(2));
        if i.saturating_add(1) >= self.queued.len() {
            return None;
        }
        let t = (*position - i as f64) as f32;
        let result = self.queued[i] * (1.0 - t) + self.queued[i + 1] * t;
        *position += step;
        let consumed = (*position as usize).min(self.queued.len().saturating_sub(1));
        self.queued.drain(..consumed);
        *position -= consumed as f64;
        Some(result)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::mpsc::sync_channel;

    #[test]
    fn streams_across_chunks_and_resamples_without_blocking() {
        let (tx, receiver) = sync_channel(2);
        let mut stream = StreamingSamples {
            receiver,
            looping: Arc::new(AtomicBool::new(false)),
            queued: VecDeque::new(),
            rate: 0,
            ended: false,
        };
        let mut position = 0.0;
        assert_eq!(stream.sample(&mut position, 0.5), None);
        assert!(!stream.ended);
        tx.send(StreamChunk {
            rate: 24_000,
            samples: vec![0.0, 0.25],
        })
        .unwrap();
        tx.send(StreamChunk {
            rate: 24_000,
            samples: vec![0.5, 0.75, 1.0],
        })
        .unwrap();
        drop(tx);
        let got: Vec<_> = (0..8)
            .map(|_| stream.sample(&mut position, 0.5).unwrap())
            .collect();
        assert_eq!(got, [0.0, 0.125, 0.25, 0.375, 0.5, 0.625, 0.75, 0.875]);
        assert_eq!(stream.rate, 24_000);
        assert_eq!(stream.sample(&mut position, 0.5), None);
        assert!(stream.ended);
    }

    #[test]
    fn dropping_a_receiver_wakes_a_blocked_producer() {
        let (tx, rx) = sync_channel(1);
        tx.send(StreamChunk {
            rate: 48_000,
            samples: vec![0.0, 1.0],
        })
        .unwrap();
        let worker = std::thread::spawn(move || {
            tx.send(StreamChunk {
                rate: 48_000,
                samples: vec![1.0, 0.0],
            })
            .is_err()
        });
        drop(rx);
        assert!(worker.join().unwrap());
    }
}
